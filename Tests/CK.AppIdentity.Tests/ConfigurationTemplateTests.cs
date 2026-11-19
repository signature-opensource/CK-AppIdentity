using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class ConfigurationTemplateTests
{
    [Test]
    public void a_template_entry_of_Parties_is_a_party_per_instance()
    {
        var config = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Shops/$Center/#Dev";
            // A tenant template: a tenant domain per instance.
            c["Parties:0:FullName"] = "Shop-{Name}/$Shop-{Name}";
            c["Parties:0:Instances:0"] = "Paris";
            c["Parties:0:Instances:1"] = "Lyon";
            // A remote template (the server side of a fleet): a remote per instance.
            c["Parties:1:PartyName"] = "Trolley-{Name}";
            c["Parties:1:InstanceCount"] = "3";
            c["Parties:1:Address"] = "tcp:127.0.0.1:{37300+Index}";
            // In a group too.
            c["Parties:2:DomainName"] = "Fleet";
            c["Parties:2:Parties:0:PartyName"] = "Cart-{Index}";
            c["Parties:2:Parties:0:InstanceCount"] = "2";
        } );
        config.ShouldNotBeNull();
        config.TenantDomains.Select( t => t.FullName.Path ).ShouldBe( ["Shop-Paris/$Shop-Paris/#Dev", "Shop-Lyon/$Shop-Lyon/#Dev"] );
        var remotes = config.Remotes.OfType<RemotePartyConfiguration>().ToList();
        remotes.Select( r => (r.PartyName, r.Address) ).ShouldBe( [("$Trolley-1", "tcp:127.0.0.1:37301"),
                                                                  ("$Trolley-2", "tcp:127.0.0.1:37302"),
                                                                  ("$Trolley-3", "tcp:127.0.0.1:37303"),
                                                                  ("$Cart-1", null),
                                                                  ("$Cart-2", null)] );
        remotes[3].FullName.Path.ShouldBe( "Fleet/$Cart-1/#Dev" );
    }

    [Test]
    public void an_invalid_template_is_a_configuration_error()
    {
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["FullName"] = "Shops/$Center/#Dev";
                c["Parties:0:PartyName"] = "Trolley-{Name}";
                c["Parties:0:InstanceCount"] = "0";
            } ).ShouldBeNull();
            texts.ShouldContain( t => t.Contains( "A positive integer is expected" ) );
        }
    }

    sealed class Directory : PartyComponentDirectory
    {
        public Directory( Dictionary<string, IReadOnlySet<Type>> sets )
            : base( [], new HashSet<Type>(), sets )
        {
        }
    }

    [Test]
    public void the_instances_of_a_template_use_the_set_of_the_template()
    {
        var d = new Directory( new Dictionary<string, IReadOnlySet<Type>>
        {
            { "$Center", new HashSet<Type> { typeof( int ) } },
            { "$Trolley-{Name}", new HashSet<Type> { typeof( string ) } },
            { "$Shop-{Name}-of-{Trolley.Index}", new HashSet<Type> { typeof( long ) } }
        } );
        d.IsMember( "$Trolley-Red", typeof( string ) ).ShouldBeTrue();
        d.IsMember( "$trolley-3", typeof( int ) ).ShouldBeFalse();
        d.HasTypeSet( "$Trolley-Red" ).ShouldBeTrue();
        d.IsMember( "$Shop-Lyon-of-2", typeof( long ) ).ShouldBeTrue();
        d.IsMember( "$Shop-Lyon-of-2", typeof( string ) ).ShouldBeFalse();
        // The exact name first; a name that matches nothing has no set (everything applies).
        d.IsMember( "$Center", typeof( string ) ).ShouldBeFalse();
        d.HasTypeSet( "$Trolley" ).ShouldBeFalse();
        d.IsMember( "$Other", typeof( string ) ).ShouldBeTrue();
    }

    [Test]
    public void FindTypeSetName_finds_the_set_of_an_instance()
    {
        string[] names = ["$Center", "$Trolley-{Name}", "App"];
        PartyComponentDirectory.FindTypeSetName( names, "$center" ).ShouldBe( "$Center" );
        PartyComponentDirectory.FindTypeSetName( names, "$Trolley-2" ).ShouldBe( "$Trolley-{Name}" );
        PartyComponentDirectory.FindTypeSetName( names, "$Trolley-" ).ShouldBeNull();
        PartyComponentDirectory.FindTypeSetName( names, "Other" ).ShouldBeNull();
    }

    [Test]
    public void FindTypeSetName_prefers_the_most_specific_template()
    {
        // A tenant template "${Name}" matches any party name: a fleet instance keeps the set of its fleet, whatever the
        // ordinal order of the names (a tenant of a template uses the set of its template, it never comes here).
        string[] names = ["${Name}", "$Holder", "$Z-{Name}"];
        PartyComponentDirectory.FindTypeSetName( names, "$Z-3" ).ShouldBe( "$Z-{Name}" );
        PartyComponentDirectory.FindTypeSetName( names, "$Acme" ).ShouldBe( "${Name}" );
        PartyComponentDirectory.FindTypeSetName( names, "$holder" ).ShouldBe( "$Holder" );
    }
}
