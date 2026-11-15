using CK.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace CK.AppIdentity;

/// <summary>
/// An option of a party component owner type, as the engine computed it (see <see cref="PartyComponentOptionDirectory"/>).
/// </summary>
public sealed class PartyComponentOptionInfo
{
    readonly Func<IServiceProvider, IConfiguration?, object> _factory;

    /// <summary>
    /// Initializes a new <see cref="PartyComponentOptionInfo"/>. Called by the generated code.
    /// </summary>
    /// <param name="name">The option name (its type name without the "Option" suffix).</param>
    /// <param name="optionType">The option type.</param>
    /// <param name="isEnabledByDefault">Whether the option is enabled by default.</param>
    /// <param name="requires">The options without which this one cannot be enabled (its non optional relations).</param>
    /// <param name="factory">Creates the option from the services of its owner and its configuration.</param>
    public PartyComponentOptionInfo( string name,
                                     Type optionType,
                                     bool isEnabledByDefault,
                                     IReadOnlyList<string> requires,
                                     Func<IServiceProvider, IConfiguration?, object> factory )
    {
        Name = name;
        OptionType = optionType;
        IsEnabledByDefault = isEnabledByDefault;
        Requires = requires;
        _factory = factory;
    }

    /// <summary>
    /// Gets the option name: its configuration key in the section of its owner.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the option type.
    /// </summary>
    public Type OptionType { get; }

    /// <summary>
    /// Gets whether this option is enabled when the configuration of its owner doesn't say.
    /// </summary>
    public bool IsEnabledByDefault { get; }

    /// <summary>
    /// Gets the names of the options without which this one cannot be enabled: the targets of its non optional relations
    /// (<c>[Requires]</c> and <c>[RequiredBy]</c>).
    /// </summary>
    public IReadOnlyList<string> Requires { get; }

    /// <summary>
    /// Creates the option: its <see cref="IConfiguration"/> constructor parameter, if any, is its configuration, the other
    /// ones are resolved from the services of its owner. An invalid configuration is an exception.
    /// </summary>
    /// <param name="services">The owner's services.</param>
    /// <param name="configuration">
    /// The option's section in the section of its owner. Null when the option is enabled by default or by <c>true</c>.
    /// </param>
    /// <returns>The option.</returns>
    public object Create( IServiceProvider services, IConfiguration? configuration )
    {
        Throw.CheckNotNullArgument( services );
        return _factory( services, configuration );
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}
