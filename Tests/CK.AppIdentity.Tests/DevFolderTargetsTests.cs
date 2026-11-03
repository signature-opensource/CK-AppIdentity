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

    static void CreateProject( NormalizedPath folder, string name, string? partyProperty = "true", string? devFolderProperty = null )
    {
        var project = folder.AppendPart( name );
        Directory.CreateDirectory( project );
        File.WriteAllText( project.AppendPart( $"{name}.csproj" ), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                {(partyProperty != null ? $"<CKAppIdentityParty>{partyProperty}</CKAppIdentityParty>" : "")}
                {(devFolderProperty != null ? $"<CKAppIdentityDevFolder>{devFolderProperty}</CKAppIdentityDevFolder>" : "")}
              </PropertyGroup>
              <Import Project="{TargetsPath.Path}" />
            </Project>
            """ );
        File.WriteAllText( project.AppendPart( "Program.cs" ), "return 0;" );
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

    static NormalizedPath CreateRoot() => new NormalizedPath( Path.Combine( Path.GetTempPath(), "CKDevFolder-" + Guid.NewGuid().ToString( "N" ) ) );

    // A main checkout: a ".git" folder (its "HEAD" file is what the targets look for).
    static void CreateGitFolder( NormalizedPath root )
    {
        Directory.CreateDirectory( root.AppendPart( ".git" ) );
        File.WriteAllText( root.Combine( ".git/HEAD" ), "ref: refs/heads/main\n" );
    }

    [Test]
    [CancelAfter( 180000 )]
    public async Task the_parties_write_their_manifest_and_copy_the_scripts_in_the_Dev_folder_Async()
    {
        var root = CreateRoot();
        try
        {
            CreateGitFolder( root );
            CreateProject( root, "Party.Host" );
            CreateProject( root, "Tool", partyProperty: null );
            CreateProject( root, "NotAParty", partyProperty: "false" );
            foreach( var name in (string[])["Party.Host", "Tool", "NotAParty"] )
            {
                await BuildAsync( root.AppendPart( name ) );
            }

            var parties = root.Combine( "#Dev/Parties" );
            // No guess: an executable is a party only when CKAppIdentityParty is true (CK.AppHost sets it).
            File.Exists( parties.AppendPart( "Tool.json" ) ).ShouldBeFalse( "No CKAppIdentityParty: not a party." );
            File.Exists( parties.AppendPart( "NotAParty.json" ) ).ShouldBeFalse( "CKAppIdentityParty is false." );

            var manifest = JsonDocument.Parse( File.ReadAllText( parties.AppendPart( "Party.Host.json" ) ) ).RootElement;
            manifest.GetProperty( "ProjectPath" ).GetString().ShouldBe( root.AppendPart( "Party.Host" ).Path );
            var executable = manifest.GetProperty( "Executable" ).GetString().ShouldNotBeNull();
            File.Exists( executable ).ShouldBeTrue();
            File.Exists( manifest.GetProperty( "Dll" ).GetString() ).ShouldBeTrue();

            File.ReadAllText( root.Combine( "#Dev/Start.ps1" ) ).ShouldBe( File.ReadAllText( TargetsPath.RemoveLastPart().AppendPart( "Start.ps1" ) ) );
            // The "#Dev" folder is ignored by git: nothing to commit.
            File.ReadAllText( root.Combine( "#Dev/.gitignore" ) ).Trim().ShouldBe( "*" );
        }
        finally
        {
            TestHelper.CleanupFolder( root, ensureFolderAvailable: false );
        }
    }

    [Test]
    [CancelAfter( 120000 )]
    public async Task in_a_submodule_or_a_linked_worktree_the_Dev_folder_is_in_it_Async()
    {
        var root = CreateRoot();
        try
        {
            // A main checkout that contains a submodule (a ".git" file): the closest one is the solution.
            CreateGitFolder( root );
            var sub = root.AppendPart( "Sub" );
            Directory.CreateDirectory( sub );
            File.WriteAllText( sub.AppendPart( ".git" ), "gitdir: ../.git/modules/Sub\n" );
            CreateProject( sub, "Party.Host" );
            await BuildAsync( sub.AppendPart( "Party.Host" ) );
            File.Exists( sub.Combine( "#Dev/Parties/Party.Host.json" ) ).ShouldBeTrue();
            Directory.Exists( root.AppendPart( "#Dev" ) ).ShouldBeFalse();
        }
        finally
        {
            TestHelper.CleanupFolder( root, ensureFolderAvailable: false );
        }
    }

    [Test]
    [CancelAfter( 120000 )]
    public async Task outside_a_git_working_folder_or_with_the_opt_out_nothing_is_written_Async()
    {
        var root = CreateRoot();
        try
        {
            CreateProject( root, "Party.Host" );
            await BuildAsync( root.AppendPart( "Party.Host" ) );
            Directory.Exists( root.AppendPart( "#Dev" ) ).ShouldBeFalse();

            CreateGitFolder( root );
            CreateProject( root, "OptOut.Host", devFolderProperty: "false" );
            await BuildAsync( root.AppendPart( "OptOut.Host" ) );
            Directory.Exists( root.AppendPart( "#Dev" ) ).ShouldBeFalse();
        }
        finally
        {
            TestHelper.CleanupFolder( root, ensureFolderAvailable: false );
        }
    }
}
