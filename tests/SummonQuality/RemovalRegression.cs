using System.Reflection;
using System.Runtime.CompilerServices;

internal static class RemovalRegression
{
    internal static void Run(Assembly mod, Action<bool, string> check)
    {
        Type tagType = mod.GetType("SecondaryAttacks.SummonQualityPresetTag", true)!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        MethodInfo counts = tagType.GetMethod("CountsTowardLimit", flags)!;
        MethodInfo begin = tagType.GetMethod("TryBeginRemoval", flags)!;
        MethodInfo cancel = tagType.GetMethod("CancelRemoval", flags)!;
        // Only managed fields/methods are exercised on these uninitialized tags.
        // No Unity component constructor, native object or real network is used.
        object NewTag() => RuntimeHelpers.GetUninitializedObject(tagType);
        bool Counts(object tag, bool valid, bool dead, double now) =>
            (bool)counts.Invoke(tag, new object[] { valid, dead, now })!;
        bool Begin(object tag, double now) => (bool)begin.Invoke(tag, new object[] { now })!;

        object pending = NewTag();
        check(Counts(pending, true, false, 0d), "Fresh live summon must count");
        check(!Counts(pending, false, false, 0d), "ZDO-cleared, not-yet-destroyed summon must not count");
        check(!Counts(pending, true, true, 0d), "Dead summon must not count");
        check(Begin(pending, 10d), "First removal request must start");
        check(!Begin(pending, 10d), "Reentrant removal request must be suppressed");
        check(!Counts(pending, true, false, 14.99d), "Remote removal awaiting delivery must not count");
        check(!Begin(pending, 14.99d), "Repeated removal before timeout must be suppressed");
        check(Counts(pending, true, false, 15d), "Lost request must become eligible again");
        check(Begin(pending, 15d), "Timed-out request must be retryable");
        cancel.Invoke(pending, null);
        check(Counts(pending, true, false, 15d), "Synchronous RPC failure must cancel pending exclusion");
        check(Begin(pending, 15d), "Synchronous RPC failure must allow retry");
        check(!Counts(pending, false, false, 100d), "Destroyed summon must stay excluded after timeout");
        check(Counts(NewTag(), true, false, 15d), "Replacement summon must not inherit another instance's request");
        Console.WriteLine("PASS compiled removal state: invalid/dead views, reentry, remote delay, timeout and failure");

        // Replay the source-reviewed callback order. Network destruction and the
        // end-of-frame Character list are simulated; eligibility is production IL.
        foreach (bool remote in new[] { false, true })
        foreach (int limit in new[] { 1, 4, 5, 10 })
        {
            int count = limit + 1;
            object[] tags = Enumerable.Range(0, count).Select(_ => NewTag()).ToArray();
            bool[] valid = Enumerable.Repeat(true, count).ToArray();
            var requested = new HashSet<int>();
            int modRequests = 0;
            void Deliver(int index) => valid[index] = false;
            // Vanilla removes the oldest A. Local Destroy resets its ZDO now,
            // but A remains in this list until the end of the frame.
            requested.Add(0);
            if (!remote) Deliver(0);
            void Enforce(double now)
            {
                int[] candidates = Enumerable.Range(0, count)
                    .Where(i => Counts(tags[i], valid[i], false, now))
                    .OrderByDescending(i => valid[i] ? count - i : 0)
                    .ToArray();
                foreach (int index in candidates.Take(Math.Max(0, candidates.Length - limit)))
                {
                    if (!Begin(tags[index], now)) continue;
                    modRequests++;
                    requested.Add(index);
                    if (!remote) Deliver(index);
                }
            }
            Enforce(20d); // RPC_Command postfix
            Enforce(20d); // Spawn coroutine finally
            Enforce(21d); // another check while a remote RPC is in flight
            check(requested.SetEquals(new[] { 0 }), $"limit {limit}, remote {remote}: only oldest summon may be removed");
            check(modRequests == (remote ? 1 : 0), "Mod must not send repeated requests while pending");
            foreach (int index in requested) Deliver(index);
            Enforce(30d); // destruction acknowledged, even beyond retry deadline
            check(valid.Count(v => v) == limit, $"limit {limit}, remote {remote}: survivor count changed");
            check(valid[^1], "Newly spawned summon must survive");
        }
        Console.WriteLine("PASS modeled vanilla/postfix/finally sequence: limits 1/4/5/10, local and delayed remote destruction");
    }
}
