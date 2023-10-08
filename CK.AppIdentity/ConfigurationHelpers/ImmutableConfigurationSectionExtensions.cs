using CK.Core;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;

namespace CK.AppIdentity
{
    /// <summary>
    /// Provides helpers to <see cref="ImmutableConfigurationSection"/>.
    /// </summary>
    public static class ImmutableConfigurationSectionExtensions
    {
        /// <summary>
        /// Emits an error if the configuration key exists and returns false.
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="reasonPhrase">The reason why this property must not be defined here.</param>
        /// <returns>True on success (no configuration), false if the key exists.</returns>
        public static bool CheckNotExist( this ImmutableConfigurationSection s, IActivityMonitor monitor, string key, string reasonPhrase )
        {
            if( s.TryGetSection( key ) != null )
            {
                monitor.Error( $"Invalid '{s.Path}:{key}' key: {reasonPhrase}" );
                return false;
            }
            return true;
        }

        /// <summary>
        /// Lookups a "true"/"false" value in this section or above.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed, emits a log warning
        /// and returns null.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <returns>The value or null.</returns>
        public static bool? TryLookupBooleanValue( this ImmutableConfigurationSection s,
                                                   IActivityMonitor monitor,
                                                   string key )
        {
            return TryReadBoolean( s, monitor, key, s.TryLookupValue( key ) );
        }

        /// <summary>
        /// Tries to get a "true"/"false" value in this section.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed, emits a log warning
        /// and returns null.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <returns>The value or null.</returns>
        public static bool? TryGetBooleanValue( this ImmutableConfigurationSection s,
                                                IActivityMonitor monitor,
                                                string key )
        {
            return TryReadBoolean( s, monitor, key, s[key] );
        }

        private static bool? TryReadBoolean( ImmutableConfigurationSection s, IActivityMonitor monitor, string key, string? a )
        {
            if( a == null ) return null;
            if( !bool.TryParse( a, out var value ) )
            {
                return WarnAndIgnore<bool>( s, monitor, key, "'true' or 'false'", a );
            }
            return value;
        }

        /// <summary>
        /// Lookups an integer value in this section or above.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed or falls outside
        /// of the <paramref name="min"/>-<paramref name="max"/> range, emits a log warning
        /// and returns null.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="min">The minimal value to accept.</param>
        /// <param name="max">The maximal value to accept.</param>
        /// <returns>The value or null.</returns>
        public static int? TryLookupIntValue( this ImmutableConfigurationSection s,
                                              IActivityMonitor monitor,
                                              string key,
                                              int min = 0,
                                              int max = int.MaxValue )
        {
            return TryReadInt( s, monitor, key, min, max, s.TryLookupValue( key ) );
        }

        /// <summary>
        /// Tries to get an integer value in this section.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed or falls outside
        /// of the <paramref name="min"/>-<paramref name="max"/> range, emits a log warning
        /// and returns null.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="min">The minimal value to accept.</param>
        /// <param name="max">The maximal value to accept.</param>
        /// <returns>The value or null.</returns>
        public static int? TryGetIntValue( this ImmutableConfigurationSection s,
                                           IActivityMonitor monitor,
                                           string key,
                                           int min = 0,
                                           int max = int.MaxValue )
        {
            return TryReadInt( s, monitor, key, min, max, s[key] );
        }

        private static int? TryReadInt( ImmutableConfigurationSection s, IActivityMonitor monitor, string key, int min, int max, string? a )
        {
            if( a == null ) return null;
            if( !int.TryParse( a, out var value ) )
            {
                return WarnAndIgnore<int>( s, monitor, key, "an integer", a );
            }
            if( value < min || value > max )
            {
                return WarnOutOfRangeAndIgnore<int>( s, monitor, key, value, min, max );
            }
            return value;
        }

        /// <summary>
        /// Lookups a <see cref="TimeSpan"/> value in this section or above.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed or falls outside
        /// of the <paramref name="min"/>-<paramref name="max"/> range, emits a log warning
        /// and returns null.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="min">The minimal value to accept.</param>
        /// <param name="max">The maximal value to accept.</param>
        /// <returns>The value or null.</returns>
        public static TimeSpan? TryLookupTimeSpanValue( this ImmutableConfigurationSection s,
                                                        IActivityMonitor monitor,
                                                        string key,
                                                        TimeSpan min,
                                                        TimeSpan max )
        {
            return TryReadTimeSpan( s, monitor, key, min, max, s.TryLookupValue( key ) );
        }

        /// <summary>
        /// Tries to get a <see cref="TimeSpan"/> value in this section.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed or falls outside
        /// of the <paramref name="min"/>-<paramref name="max"/> range, emits a log warning
        /// and returns null.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="min">The minimal value to accept.</param>
        /// <param name="max">The maximal value to accept.</param>
        /// <returns>The value or null.</returns>
        public static TimeSpan? TryGetTimeSpanValue( this ImmutableConfigurationSection s,
                                                     IActivityMonitor monitor,
                                                     string key,
                                                     TimeSpan min,
                                                     TimeSpan max )
        {
            return TryReadTimeSpan( s, monitor, key, min, max, s[key] );
        }

        static TimeSpan? TryReadTimeSpan( ImmutableConfigurationSection s, IActivityMonitor monitor, string key, TimeSpan min, TimeSpan max, string? a )
        {
            if( a == null ) return null;
            if( !TimeSpan.TryParse( a, out var value ) )
            {
                return WarnAndIgnore<TimeSpan>( s, monitor, key, "a TimeSpan", a );
            }
            if( value < min || value > max )
            {
                return WarnOutOfRangeAndIgnore<TimeSpan>( s, monitor, key, value, min, max );
            }
            return value;
        }

        /// <summary>
        /// Lookups a floating number value in this section or above.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed or falls outside
        /// of the <paramref name="min"/>-<paramref name="max"/> range, emits a log warning
        /// and returns null.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="min">The minimal value to accept.</param>
        /// <param name="max">The maximal value to accept.</param>
        /// <returns>The value or null.</returns>
        public static double? TryLookupDoubleValue( this ImmutableConfigurationSection s,
                                                    IActivityMonitor monitor,
                                                    string key,
                                                    double min = 0.0,
                                                    double max = double.MaxValue )
        {
            return TryReadDouble( s, monitor, key, min, max, s.TryLookupValue( key ) );
        }

        /// <summary>
        /// Tries to get a floating number value in this section.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed or falls outside
        /// of the <paramref name="min"/>-<paramref name="max"/> range, emits a log warning
        /// and returns null.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="min">The minimal value to accept.</param>
        /// <param name="max">The maximal value to accept.</param>
        /// <returns>The value or null.</returns>
        public static double? TryGetDoubleValue( this ImmutableConfigurationSection s,
                                                 IActivityMonitor monitor,
                                                 string key,
                                                 double min = 0.0,
                                                 double max = double.MaxValue )
        {
            return TryReadDouble( s, monitor, key, min, max, s[key] );
        }

        static double? TryReadDouble( ImmutableConfigurationSection s, IActivityMonitor monitor, string key, double min, double max, string? a )
        {
            if( a == null ) return null;
            if( !double.TryParse( a, out var value ) )
            {
                return WarnAndIgnore<double>( s, monitor, key, "a number", a );
            }
            if( value < min || value > max )
            {
                return WarnOutOfRangeAndIgnore<double>( s, monitor, key, value, min, max );
            }
            return value;
        }

        /// <summary>
        /// Lookups an enum value in this section or above.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed, emits a log warning
        /// and returns null.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <returns>The value or null.</returns>
        public static T? TryLookupEnumValue<T>( this ImmutableConfigurationSection s,
                                                IActivityMonitor monitor,
                                                string key )
            where T : struct, Enum
        {
            return TryReadEnum<T>( s, monitor, key, s.TryLookupValue( key ) );
        }

        /// <summary>
        /// Tries to get an enum value in this section.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed, emits a log warning
        /// and returns null.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <returns>The value or null.</returns>
        public static T? TryGetEnumValue<T>( this ImmutableConfigurationSection s,
                                             IActivityMonitor monitor,
                                             string key )
            where T : struct, Enum
        {
            return TryReadEnum<T>( s, monitor, key, s[key] );
        }

        static T? TryReadEnum<T>( ImmutableConfigurationSection s, IActivityMonitor monitor, string key, string? a ) where T : struct, Enum
        {
            if( a == null ) return null;
            if( !Enum.TryParse<T>( a, true, out var value ) )
            {
                return WarnAndIgnore<T>( s, monitor, key, $"a {typeof( T ).Name} value", a );
            }
            return value;
        }

        static T? WarnOutOfRangeAndIgnore<T>( ImmutableConfigurationSection s, IActivityMonitor monitor, string key, T value, T min, T max )
        {
            monitor.Warn( $"Invalid '{s.Path}:{key}': value '{value}' must be between '{min}' and '{max}'. Ignored." );
            return default;
        }

        static T? WarnAndIgnore<T>( ImmutableConfigurationSection s,
                                    IActivityMonitor monitor,
                                    string key,
                                    string expected,
                                    string? a )
        {
            monitor.Warn( $"Unable to parse '{s.Path}:{key}' value, expected {expected} but got '{a}'. Ignored." );
            return default;
        }


        /// <summary>
        /// Lookups a "true"/"false" (case insensitive) boolean value in this section or above.
        /// <para>
        /// If the value exists and cannot be parsed, emits a log warning and returns the <paramref name="defaultValue"/>.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="defaultValue">Returned default value.</param>
        /// <returns>The value.</returns>
        public static bool LookupBooleanValue( this ImmutableConfigurationSection s,
                                                IActivityMonitor monitor,
                                                string key,
                                                bool defaultValue = false )
        {
            var a = s.TryLookupValue( key );
            if( a == null ) return defaultValue;
            if( !bool.TryParse( a, out var value ) )
            {
                return WarnWithDefault( s, monitor, key, "'true' or 'false'", defaultValue, a );
            }
            return value;
        }

        /// <summary>
        /// Lookups an integer value in this section or above.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed, emits a log warning
        /// and returns the <paramref name="defaultValue"/>.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="defaultValue">Returned default value.</param>
        /// <returns>The value.</returns>
        public static int LookupIntValue( this ImmutableConfigurationSection s,
                                          IActivityMonitor monitor,
                                          string key,
                                          int defaultValue = 0 )
        {
            var a = s.TryLookupValue( key );
            if( a == null ) return defaultValue;
            if( !int.TryParse( a, out var value ) )
            {
                return WarnWithDefault( s, monitor, key, "an integer", defaultValue, a );
            }
            return value;
        }

        /// <summary>
        /// Lookups a <see cref="TimeSpan"/> value in this section or above.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed, emits a log warning
        /// and returns the <paramref name="defaultValue"/>.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="defaultValue">Returned default value.</param>
        /// <returns>The value.</returns>
        public static TimeSpan LookupTimeSpanValue( this ImmutableConfigurationSection s,
                                                    IActivityMonitor monitor,
                                                    string key,
                                                    TimeSpan defaultValue )
        {
            var a = s.TryLookupValue( key );
            if( a == null ) return defaultValue;
            if( !TimeSpan.TryParse( a, out var value ) )
            {
                return WarnWithDefault( s, monitor, key, "a TimeSpan", defaultValue, a );
            }
            return value;
        }

        /// <summary>
        /// Lookups a float value in this section or above.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed, emits a log warning
        /// and returns the <paramref name="defaultValue"/>.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="defaultValue">Returned default value.</param>
        /// <returns>The value.</returns>
        public static double LookupDoubleValue( this ImmutableConfigurationSection s,
                                                IActivityMonitor monitor,
                                                string key,
                                                double defaultValue = 0.0 )
        {
            var a = s.TryLookupValue( key );
            if( a == null ) return defaultValue;
            if( !double.TryParse( a, out var value ) )
            {
                return WarnWithDefault( s, monitor, key, "a float number", defaultValue, a );
            }
            return value;
        }

        /// <summary>
        /// Lookups an enum value in this section or above.
        /// <para>
        /// This never throws: if the value exists and cannot be parsed, emits a log warning
        /// and returns the <paramref name="defaultValue"/>.
        /// </para>
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="defaultValue">Returned default value.</param>
        /// <returns>The value.</returns>
        public static T? LookupEnumValue<T>( this ImmutableConfigurationSection s,
                                             IActivityMonitor monitor,
                                             string key,
                                             T defaultValue )
              where T : struct, Enum
        {
            var a = s.TryLookupValue( key );
            if( a == null ) return defaultValue;
            if( !Enum.TryParse<T>( a, true, out var value ) )
            {
                return WarnWithDefault( s, monitor, key, $"a {typeof(T).Name} value", defaultValue, a );
            }
            return value;
        }

        static T WarnWithDefault<T>( ImmutableConfigurationSection s,
                                     IActivityMonitor monitor,
                                     string key,
                                     string expected,
                                     T defaultValue,
                                     string? a )
        {
            monitor.Warn( $"Unable to parse '{s.Path}:{key}' value, expected {expected} but got '{a}'. Using default '{defaultValue}'." );
            return defaultValue;
        }

        /// <summary>
        /// Helper that reads a string array from a string value, a comma separated string, or children
        /// sections (with string value or comma separated string) that must have integer keys ("0", "1",...).
        /// Returns null on error (and the error is logged).
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="key">The configuration key.</param>
        /// <returns>The string array (empty if the key doesn't exist) or null on error.</returns>
        public static string[]? ReadStringArray( ImmutableConfigurationSection s, IActivityMonitor monitor, string key )
        {
            return s.TryGetSection( key ).ReadStringArray( monitor );
        }

        /// <summary>
        /// Helper that reads a string array from a string value, a comma separated string, or children
        /// sections (with string value or comma separated string) that must have integer keys ("0", "1",...).
        /// Returns null on error (and the error is logged).
        /// </summary>
        /// <param name="s">This section.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The string array (empty if the section is null) or null on error.</returns>
        public static string[]? ReadStringArray( this ImmutableConfigurationSection? s, IActivityMonitor monitor )
        {
            if( s != null )
            {
                if( s.Value != null )
                {
                    return s.Value.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.RemoveEmptyEntries );
                }
                var result = new List<string>();
                foreach( var o in s.GetChildren() )
                {
                    var value = o.Value;
                    if( value == null || !int.TryParse( o.Key, out _ ) )
                    {
                        monitor.Error( $"Invalid array configuration for '{s.Path}': key '{o.Path}' is invalid." );
                        return null;
                    }
                    if( string.IsNullOrEmpty( value ) ) continue;
                    if( value.Contains( ',' ) ) result.AddRangeArray( value.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.RemoveEmptyEntries ) );
                    else
                    {
                        value = value.Trim();
                        if( value.Length > 0 ) result.Add( value );
                    }
                }
                return result.ToArray();
            }
            return Array.Empty<string>();
        }

        /// <summary>
        /// Calls <see cref="ReadStringArray(ImmutableConfigurationSection, IActivityMonitor)"/> and ensures that
        /// strings are unique.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="s">The section.</param>
        /// <param name="comparer">Optional comparer.</param>
        /// <returns>A set of unique strings or null on error.</returns>
        public static HashSet<string>? ReadUniqueStringSet( this ImmutableConfigurationSection? s, IActivityMonitor monitor, StringComparer? comparer = null )
        {
            var a = ReadStringArray( s, monitor );
            if( a == null ) return null;
            var set = new HashSet<string>( a, comparer );
            if( set.Count != a.Length )
            {
                Throw.DebugAssert( s != null, "Since we found something." );
                monitor.Error( $"Duplicate found in '{s.Path}': {a.Except( set ).Concatenate()}." );
                return null;
            }
            return set;
        }

        /// <summary>
        /// Calls <see cref="ReadStringArray(ImmutableConfigurationSection, IActivityMonitor, string)"/> and ensures that
        /// strings are unique.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="s">The section.</param>
        /// <param name="key">The configuration key.</param>
        /// <param name="comparer">Optional comparer.</param>
        /// <returns>A set of unique strings or null on error.</returns>
        public static HashSet<string>? ReadUniqueStringSet( this ImmutableConfigurationSection s, IActivityMonitor monitor, string key, StringComparer? comparer = null )
        {
            return s.TryGetSection( key ).ReadUniqueStringSet( monitor, comparer );
        }
    }
}
