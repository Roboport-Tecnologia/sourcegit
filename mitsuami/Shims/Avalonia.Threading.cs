using System;
using System.Threading.Tasks;

using Mitsuamia;

namespace Avalonia.Threading
{
    public enum DispatcherPriority
    {
        Background,
        Normal,
        Render,
        Send,
    }

    /// <summary>
    /// Avalonia's UI-thread dispatcher, on mitsuami's run loop. Priorities are
    /// ignored: mitsuami runs posted work in order. Before the app runs (no
    /// UI thread yet), work runs where it's called, as on Avalonia's thread
    /// before its loop starts.
    /// </summary>
    public sealed class Dispatcher
    {
        public static Dispatcher UIThread { get; } = new();

        private static bool Running => Mitsuamia.Application.IsRunning;

        public bool CheckAccess() => !Running || Mitsuamia.Application.IsUiThread;

        public void VerifyAccess()
        {
            if (!CheckAccess())
                throw new InvalidOperationException("Call from invalid thread");
        }

        public void Post(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            if (Running)
                Mitsuamia.Application.Post(action);
            else
                action();
        }

        public void Invoke(Action action, DispatcherPriority priority = DispatcherPriority.Send)
        {
            if (CheckAccess())
            {
                action();
                return;
            }

            Exception error = null;
            using var done = new System.Threading.ManualResetEventSlim();
            Mitsuamia.Application.Post(() =>
            {
                try { action(); }
                catch (Exception e) { error = e; }
                finally { done.Set(); }
            });
            done.Wait();
            if (error != null)
                throw error;
        }

        public T Invoke<T>(Func<T> func, DispatcherPriority priority = DispatcherPriority.Send)
        {
            T result = default;
            Invoke(() => { result = func(); }, priority);
            return result;
        }

        public Task InvokeAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            if (CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Mitsuamia.Application.Post(() =>
            {
                try
                {
                    action();
                    done.SetResult();
                }
                catch (Exception e)
                {
                    done.SetException(e);
                }
            });
            return done.Task;
        }

        public Task<T> InvokeAsync<T>(Func<T> func, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            if (CheckAccess())
                return Task.FromResult(func());

            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mitsuamia.Application.Post(() =>
            {
                try { done.SetResult(func()); }
                catch (Exception e) { done.SetException(e); }
            });
            return done.Task;
        }
    }
}
