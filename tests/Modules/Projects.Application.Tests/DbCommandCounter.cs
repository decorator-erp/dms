using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Projects.Application.Tests;

/// <summary>
/// Counts the database commands EF issues inside one <see cref="Begin"/> scope, across every
/// <c>DbContext</c> of every module — the Projects handler, its decorators and the contract readers it calls.
///
/// <para>Listens on EF's process-wide <see cref="DiagnosticListener"/>, so it needs no change to any
/// module's DI registration. The count is keyed on an <see cref="AsyncLocal{T}"/>: EF raises the event
/// synchronously on the executing async flow, so commands from tests running concurrently in other
/// collections never land in this scope's count.</para>
/// </summary>
internal static class DbCommandCounter
{
    private static readonly AsyncLocal<Counts?> Current = new();
    private static int _subscribed;

    internal sealed class Counts
    {
        private int _queries;
        private int _saves;

        /// <summary>Commands from anything but <c>SaveChanges</c>: LINQ queries, raw SQL, locks.</summary>
        public int Queries => _queries;

        /// <summary><c>SaveChanges</c> batches. These grow with the row count by design, one batch per
        /// provider batch-size worth of statements, so they are reported apart from the queries.</summary>
        public int SaveBatches => _saves;

        internal void Add(CommandSource source)
        {
            if (source == CommandSource.SaveChanges)
            {
                Interlocked.Increment(ref _saves);
            }
            else
            {
                Interlocked.Increment(ref _queries);
            }
        }
    }

    public static Counts Begin()
    {
        EnsureSubscribed();
        var counts = new Counts();
        Current.Value = counts;
        return counts;
    }

    public static void End() => Current.Value = null;

    private static void EnsureSubscribed()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 1)
        {
            return;
        }

        DiagnosticListener.AllListeners.Subscribe(new ListenerObserver());
    }

    private sealed class ListenerObserver : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == DbLoggerCategory.Name)
            {
                value.Subscribe(new EventObserver(), name => name == RelationalEventId.CommandExecuting.Name);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class EventObserver : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is CommandEventData data && Current.Value is { } counts)
            {
                counts.Add(data.CommandSource);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
