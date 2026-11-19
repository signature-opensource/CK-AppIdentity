using CK.CodeGen;
using CK.Core;
using System;
using System.Linq;

namespace CK.Setup;

/// <summary>
/// Code generator of the <see cref="CK.AppIdentity.CompiledConfiguration"/>: the values burnt by the App builder in the
/// BinPath (<see cref="BinPathConfiguration.CompiledValues"/> and <see cref="BinPathConfiguration.CompiledLocks"/>), as
/// literals.
/// </summary>
public sealed class CompiledConfigurationImpl : CSCodeGeneratorType
{
    /// <inheritdoc />
    public override CSCodeGenerationResult Implement( IActivityMonitor monitor, Type classType, ICSCodeGenerationContext c, ITypeScope scope )
    {
        var group = c.CurrentRun.ConfigurationGroup;
        // The purely unified BinPath has no host: nothing is burnt.
        var configuration = group.IsUnifiedPure ? null : group.Configuration;
        scope.NewLine().Append( "#nullable enable" ).NewLine();
        scope.Append( "static readonly global::System.Collections.Generic.Dictionary<string, string?> _values = new( global::System.StringComparer.OrdinalIgnoreCase ) {" ).NewLine();
        if( configuration != null )
        {
            foreach( var (path, value) in configuration.CompiledValues.OrderBy( kv => kv.Key, StringComparer.OrdinalIgnoreCase ) )
            {
                scope.Append( "{ " ).AppendSourceString( path ).Append( ", " );
                if( value == null ) scope.Append( "null" ); else scope.AppendSourceString( value );
                scope.Append( " }," ).NewLine();
            }
        }
        scope.Append( "};" ).NewLine()
             .Append( "static readonly string[] _locks = new string[] {" );
        if( configuration != null )
        {
            foreach( var l in configuration.CompiledLocks.Order( StringComparer.OrdinalIgnoreCase ) ) scope.AppendSourceString( l ).Append( ", " );
        }
        scope.Append( "};" ).NewLine()
             .Append( "public override global::System.Collections.Generic.IReadOnlyDictionary<string, string?> Values => _values;" ).NewLine()
             .Append( "public override global::System.Collections.Generic.IReadOnlyList<string> Locks => _locks;" ).NewLine()
             .Append( "#nullable restore" ).NewLine();
        if( configuration != null && configuration.CompiledValues.Count > 0 )
        {
            monitor.Info( $"{configuration.CompiledValues.Count} configuration value(s) burnt: '{configuration.CompiledValues.Keys.Order().Concatenate( "', '" )}'." );
        }
        return CSCodeGenerationResult.Success;
    }
}
