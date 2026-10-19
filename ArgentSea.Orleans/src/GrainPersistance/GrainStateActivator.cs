// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System.Reflection;

namespace ArgentSea.Orleans
{
    /// <summary>
    /// Invokes the static <see cref="IActivator{TModel}.Activatate{Tmodel}(string, ReadOnlyMemory{byte})"/> factory of grain state types which implement <see cref="IActivator{TModel}"/>.
    /// </summary>
    internal static class GrainStateActivator
    {
        /// <summary>
        /// Creates the grain state with the type’s <see cref="IActivator{TModel}"/> factory, if it implements one.
        /// </summary>
        /// <returns>True if <typeparamref name="TModel"/> implements <see cref="IActivator{TModel}"/> and the instance was created by it; otherwise false.</returns>
        internal static bool TryActivate<TModel>(string grainType, ReadOnlyMemory<byte> key, out TModel instance)
        {
            var factory = FactoryCache<TModel>.Factory;
            if (factory is null)
            {
                instance = default!;
                return false;
            }
            instance = factory(grainType, key);
            return true;
        }

        private static TModel Activate<TModel>(string grainType, ReadOnlyMemory<byte> key) where TModel : IActivator<TModel>
            => TModel.Activatate<TModel>(grainType, key);

        private static class FactoryCache<TModel>
        {
            internal static readonly Func<string, ReadOnlyMemory<byte>, TModel>? Factory = Build();

            private static Func<string, ReadOnlyMemory<byte>, TModel>? Build()
            {
                if (!typeof(IActivator<TModel>).IsAssignableFrom(typeof(TModel)))
                {
                    return null;
                }
                var mi = typeof(GrainStateActivator)
                    .GetMethod(nameof(Activate), BindingFlags.Static | BindingFlags.NonPublic)!
                    .MakeGenericMethod(typeof(TModel));
                return mi.CreateDelegate<Func<string, ReadOnlyMemory<byte>, TModel>>();
            }
        }
    }
}
