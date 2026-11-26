using CK.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace CK.AppIdentity;

/// <summary>
/// An option of a party component owner type, as the engine computed it (see <see cref="PartyComponentOptionDirectory"/>).
/// </summary>
public sealed class PartyComponentOptionInfo : IConfigurationOption
{
    readonly Func<IServiceProvider, object?, object> _factory;

    /// <summary>
    /// Initializes a new <see cref="PartyComponentOptionInfo"/>. Called by the generated code.
    /// </summary>
    /// <param name="name">The option name (declared by its attribute: its key in the section of its owner).</param>
    /// <param name="optionType">The option type.</param>
    /// <param name="activation">The activation of the option.</param>
    /// <param name="requires">The options without which this one cannot be enabled (its non optional relations).</param>
    /// <param name="factory">Creates the option from the services of its owner and its configuration (its section, or its model when it has one).</param>
    /// <param name="model">The description of the option's configuration model (<c>[Configuration&lt;TModel&gt;]</c>), if any.</param>
    /// <param name="requiredFeatures">
    /// The features that this option requires (its non optional <c>[Requires]</c> to a party feature driver, or, for
    /// an option of a client feature, to another client feature): it is enabled only when their feature is allowed on
    /// the party of its owner (a party feature) or is a feature of the client of its owner (a client feature).
    /// </param>
    public PartyComponentOptionInfo( string name,
                                     Type optionType,
                                     TypeActivation activation,
                                     IReadOnlyList<string> requires,
                                     Func<IServiceProvider, object?, object> factory,
                                     ConfigurationModelDescription? model = null,
                                     IReadOnlyList<Type>? requiredFeatures = null )
    {
        Name = name;
        OptionType = optionType;
        Activation = activation;
        Requires = requires;
        _factory = factory;
        Model = model;
        RequiredFeatures = requiredFeatures ?? [];
    }

    /// <summary>
    /// Gets the features that this option requires: the party feature drivers (their feature is allowed on the party of
    /// its owner) and, for an option of a client feature, the client features (a feature of the client of its owner).
    /// It is enabled only with them (the "only with it" rule of <see cref="ConfigurationOptionSelector"/>).
    /// </summary>
    public IReadOnlyList<Type> RequiredFeatures { get; }

    /// <summary>
    /// Names the first required feature (see <see cref="RequiredFeatures"/>) that is not allowed on a party: for the
    /// <c>findMissingRequirement</c> of <see cref="ConfigurationOptionSelector"/>.
    /// </summary>
    /// <param name="services">The services where the feature drivers are.</param>
    /// <param name="party">The party of the owner of this option.</param>
    /// <param name="findClientFeature">
    /// For an option of a client feature: the name of a required client feature when the client of the owner has it,
    /// null otherwise. Without it, a required client feature is missing.
    /// </param>
    /// <returns>Null when every required feature is there, "the party feature 'Name'" (or "the client feature 'Name'") otherwise.</returns>
    public string? FindMissingFeature( IServiceProvider services, IParty party, Func<Type, string?>? findClientFeature = null )
    {
        foreach( var t in RequiredFeatures )
        {
            if( !typeof( ApplicationIdentityFeatureDriver ).IsAssignableFrom( t ) )
            {
                if( findClientFeature?.Invoke( t ) == null ) return $"the client feature '{t.Name}'";
                continue;
            }
            if( services.GetService( t ) is not ApplicationIdentityFeatureDriver d )
            {
                return $"the party feature '{t.ToCSharpName()}' (not a service)";
            }
            if( !d.IsAllowedFeature( party ) ) return $"the party feature '{d.GetFeatureName( party )}'";
        }
        return null;
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
    /// Gets the activation of this option: <see cref="TypeActivation.Optional"/>, enabled only when the configuration of its
    /// owner says so; <see cref="TypeActivation.ByDefault"/>, enabled unless it says <c>false</c>; <see cref="TypeActivation.Required"/>,
    /// always enabled.
    /// </summary>
    public TypeActivation Activation { get; }

    /// <summary>
    /// Gets whether this option is enabled when the configuration of its owner doesn't say: its <see cref="Activation"/>
    /// is not <see cref="TypeActivation.Optional"/>.
    /// </summary>
    public bool IsEnabledByDefault => Activation != TypeActivation.Optional;

    /// <summary>
    /// Gets whether this option is always enabled: its <see cref="Activation"/> is <see cref="TypeActivation.Required"/>.
    /// </summary>
    public bool IsRequired => Activation == TypeActivation.Required;

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
    public object Create( IServiceProvider services, object? configuration )
    {
        Throw.CheckNotNullArgument( services );
        return _factory( services, configuration );
    }

    /// <summary>
    /// Gets the description of the option's configuration model (<c>[Configuration&lt;TModel&gt;]</c>): the option receives
    /// its model, read key by key along the levels of its owner's configuration. Null when the option reads its
    /// <see cref="IConfiguration"/>.
    /// </summary>
    public ConfigurationModelDescription? Model { get; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
