using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity.Tests;

[TestFixture]
public class MicroAgentTests
{
    sealed class TestAgent : MicroAgent
    {
        public TestAgent() : base( "TestAgent" ) { }

        public readonly TaskCompletionSource StopEntered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public readonly TaskCompletionSource ReleaseStop = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public RunningStatus Start() => TryStart();

        public bool Stop() => SendStop();

        protected override async ValueTask OnStopAsync( IActivityMonitor monitor )
        {
            StopEntered.SetResult();
            await ReleaseStop.Task;
        }
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task the_Post_methods_return_false_as_soon_as_the_agent_is_stopping_Async( CancellationToken cancel )
    {
        var agent = new TestAgent();
        agent.Start().ShouldBe( MicroAgent.RunningStatus.Running );
        var executed = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        agent.PostAction( executed, static ( m, tcs ) => tcs.SetResult() ).ShouldBeTrue();
        await executed.Task.WaitAsync( cancel );

        agent.Stop().ShouldBeTrue();
        await agent.StopEntered.Task.WaitAsync( cancel );
        // The teardown is running: the channel is not completed yet but the actions are refused.
        int count = 0;
        agent.PostAction( 0, ( m, _ ) => ++count ).ShouldBeFalse();
        agent.PostTask( 0, ( m, _ ) => { ++count; return Task.CompletedTask; } ).ShouldBeFalse();
        agent.PostValueTask( 0, ( m, _ ) => { ++count; return default; } ).ShouldBeFalse();
        agent.ReleaseStop.SetResult();
        await agent.RunningTask.WaitAsync( cancel );
        count.ShouldBe( 0 );
        agent.PostAction( 0, ( m, _ ) => ++count ).ShouldBeFalse( "Stopped." );
    }
}
