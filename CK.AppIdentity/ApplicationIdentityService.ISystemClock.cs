using CK.Core;
using System;

namespace CK.AppIdentity
{
    public sealed partial class ApplicationIdentityService
    {
        /// <summary>
        /// Optional system clock that when available in the DI container enables to configure
        /// the <see cref="ApplicationIdentityService.Heartbeat"/> rate.
        /// <para>
        /// This should be used mainly for tests. When not registered in tne DI container, <see cref="DefaultClock"/> is used.
        /// </para>
        /// </summary>
        public interface ISystemClock : Core.ISystemClock
        {
            /// <summary>
            /// Gets the heart beat period in milliseconds.
            /// Defaults to 1000 ms (1 second that is the maximum).
            /// Must be between 20 and 1000.
            /// </summary>
            int HeatBeatPeriod { get; }
        }

        sealed class NoSystemClock : ISystemClock
        {
            public DateTime UtcNow => DateTime.UtcNow;

            public int HeatBeatPeriod => 1000;
        }

        /// <summary>
        /// Gets a default clock directly bound to <see cref="DateTime.UtcNow"/>
        /// with a 1 second (1000 ms) <see cref="ISystemClock.HeatBeatPeriod"/>.
        /// </summary>
        public static readonly ISystemClock DefaultClock = new NoSystemClock();
    }
}
