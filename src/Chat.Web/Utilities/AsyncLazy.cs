using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Chat.Web.Utilities
{
    /// <summary>
    /// Provides support for asynchronous lazy initialization.
    /// This type is thread-safe and ensures only one asynchronous initialization occurs.
    /// </summary>
    /// <typeparam name="T">The type of object that is being lazily initialized.</typeparam>
    public class AsyncLazy<T>
    {
        private readonly Lazy<Task<T>> _instance;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncLazy{T}"/> class with the specified initialization function.
        /// </summary>
        /// <param name="factory">The asynchronous initialization function.</param>
        public AsyncLazy(Func<Task<T>> factory)
        {
            _instance = new Lazy<Task<T>>(factory);
        }

        /// <summary>
        /// Gets the task that produces the value.
        /// The task is started on first access and the result is cached.
        /// </summary>
        public Task<T> Task => _instance.Value;

        /// <summary>
        /// Gets an awaiter used to await this <see cref="AsyncLazy{T}"/>.
        /// </summary>
        /// <returns>An awaiter instance.</returns>
        public TaskAwaiter<T> GetAwaiter()
        {
            return _instance.Value.GetAwaiter();
        }
    }
}
