using System;
using System.Threading;
using System.Threading.Tasks;

namespace MsBuildPipeLogger
{
    internal static class CancellationTokenExtensions
    {
        public static TResult Try<TResult>(this CancellationToken cancellationToken, Func<TResult> action, Func<TResult> cancelled)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            if (cancelled is null)
            {
                throw new ArgumentNullException(nameof(cancelled));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return cancelled();
            }
            try
            {
                return action();
            }
            catch (TaskCanceledException)
            {
                // Thrown if the task itself was canceled from inside the read method
                return cancelled();
            }
            catch (OperationCanceledException)
            {
                // Thrown if the operation was canceled (I.e., the task didn't deal with cancellation)
                return cancelled();
            }
            catch (AggregateException ex)
            {
                // Sometimes the cancellation exceptions are thrown in aggregate
                if (ex.InnerException is not TaskCanceledException
                    && ex.InnerException is not OperationCanceledException)
                {
                    throw;
                }
                return cancelled();
            }
        }
    }
}