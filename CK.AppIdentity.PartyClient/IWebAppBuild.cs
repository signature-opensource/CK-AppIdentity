using CK.Core;
using System.Diagnostics;

namespace CK.AppIdentity;

/// <summary>
/// The web build of a client: a client feature that builds a web application (an Angular application, a Vite one, a
/// Blazor WebAssembly one...) and knows where its output is and how to start its dev server. The hosting of a client
/// (the party that serves it) requires it. A client has at most one.
/// </summary>
[IsSingle]
public interface IWebAppBuild : IPartyComponentAbstraction
{
    /// <summary>
    /// Gets the path of the client's project: relative to the host's project when it is under it, absolute otherwise.
    /// </summary>
    NormalizedPath ProjectPath { get; }

    /// <summary>
    /// Gets the path of the built application, relative to the <see cref="ProjectPath"/>.
    /// </summary>
    NormalizedPath OutputPath { get; }

    /// <summary>
    /// Gets how to start the dev server of the client in its project folder (its working directory is set by the caller).
    /// </summary>
    /// <param name="yarnPath">The path of the yarn release (a ".cjs" file) found in or above the project.</param>
    /// <param name="port">The port of the dev server.</param>
    /// <returns>The process to start.</returns>
    ProcessStartInfo GetDevServerStart( string yarnPath, int port );
}
