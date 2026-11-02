using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Tests;

/// <summary>
/// The "buildTransitive" targets of CK.AppIdentity.Hosting are only imported through a package reference: these tests
/// build small projects in a temporary folder (outside this repository: its Directory.Build.props must not apply) that
/// import them by path.
/// </summary>
[TestFixture]
public class DevFolderTargetsTests
{
    static NormalizedPath TargetsPath => TestHelper.SolutionFolder.Combine( "CK.AppIdentity.Hosting/buildTransitive/CK.AppIdentity.Hosting.targets" );

    static void CreateProject( NormalizedPath folder, string name, bool appSettings, string? partyProperty = null )
    {
        var project = folder.AppendPart( name );
        Directory.CreateDirectory( project );
        File.WriteAllText( project.AppendPart( $"{name}.csproj" ), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                {(partyProperty != null ? $"<CKAppIdentityParty>{partyProperty}</CKAppIdentityParty>" : "")}
              </PropertyGroup>
              <Import Project="{TargetsPath.Path}" />
            </Project>
            """ );
        File.WriteAllText( project.AppendPart( "Program.cs" ), "return 0;" );
        if( appSettings ) File.WriteAllText( project.AppendPart( "appsettings.json" ), "{}" );
    }

    static async Task BuildAsync( NormalizedPath project )
    {
        var info = new ProcessStartInfo( "dotnet", $"build \"{project.Path}\" -v q -nologo" )
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start( info ).ShouldNotBeNull();
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        p.ExitCode.ShouldBe( 0, output );
    }

    [Test]
    [CancelAfter( 180000 )]
    public async Task the_parties_write_their_manifest_and_copy_the_scripts_in_the_Dev_folder_Async()
    {
        var root = new NormalizedPath( Path.Combine( Path.GetTempPath(), "CKDevFolder-" + Guid.NewGuid().ToString( "N" ) ) );
        try
        {
            // The opt-in: a committed "#Dev/.gitignore".
            Directory.CreateDirectory( root.AppendPart( "#Dev" ) );
            File.WriteAllText( root.Combine( "#Dev/.gitignore" ), "*\n!.gitignore\n" );
            CreateProject( root, "Party.Host", appSettings: true );
            CreateProject( root, "Tool", appSettings: false );
            CreateProject( root, "NotAParty", appSettings: true, partyProperty: "false" );
            CreateProject( root, "ForcedParty", appSettings: false, partyProperty: "true" );
            foreach( var name in (string[])["Party.Host", "Tool", "NotAParty", "ForcedParty"] )
            {
                await BuildAsync( root.AppendPart( name ) );
            }

            var parties = root.Combine( "#Dev/Parties" );
            File.Exists( parties.AppendPart( "Tool.json" ) ).ShouldBeFalse( "No appsettings.json: not a party." );
            File.Exists( parties.AppendPart( "NotAParty.json" ) ).ShouldBeFalse( "CKAppIdentityParty is false." );
            File.Exists( parties.AppendPart( "ForcedParty.json" ) ).ShouldBeTrue( "CKAppIdentityParty is true." );

            var manifest = JsonDocument.Parse( File.ReadAllText( parties.AppendPart( "Party.Host.json" ) ) ).RootElement;
            manifest.GetProperty( "ProjectPath" ).GetString().ShouldBe( root.AppendPart( "Party.Host" ).Path );
            var executable = manifest.GetProperty( "Executable" ).GetString().ShouldNotBeNull();
            File.Exists( executable ).ShouldBeTrue();
            File.Exists( manifest.GetProperty( "Dll" ).GetString() ).ShouldBeTrue();

            File.ReadAllText( root.Combine( "#Dev/Start.ps1" ) ).ShouldBe( File.ReadAllText( TargetsPath.RemoveLastPart().AppendPart( "Start.ps1" ) ) );
        }
        finally
        {
            TestHelper.CleanupFolder( root, ensureFolderAvailable: false );
        }
    }

    [Test]
    [CancelAfter( 120000 )]
    public async Task without_a_Dev_folder_nothing_is_written_Async()
    {
        var root = new NormalizedPath( Path.Combine( Path.GetTempPath(), "CKDevFolder-" + Guid.NewGuid().ToString( "N" ) ) );
        try
        {
            CreateProject( root, "Party.Host", appSettings: true );
            await BuildAsync( root.AppendPart( "Party.Host" ) );
            Directory.Exists( root.AppendPart( "#Dev" ) ).ShouldBeFalse();
        }
        finally
        {
            TestHelper.CleanupFolder( root, ensureFolderAvailable: false );
        }
    }
}
