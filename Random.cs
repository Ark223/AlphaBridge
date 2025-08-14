﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AlphaBridge
{
    /// <summary>
    /// Thread-safe random generator using per-thread instances.
    /// </summary>
    internal class Random
    {
        /// <summary>
        /// Thread-local random generator to avoid contention.
        /// </summary>
        [ThreadStatic]
        private static System.Random _local;

        /// <summary>
        /// Global random generator used to seed thread-local instances.
        /// </summary>
        private static readonly System.Random _global = new System.Random();

        /// <summary>
        /// Gets the thread-local <see cref="System.Random"/> instance.
        /// </summary>
        private static System.Random Instance
        {
            get
            {
                if (_local == null)
                {
                    int seed;
                    lock (_global) { seed = _global.Next(); }
                    _local = new System.Random(seed);
                }
                return _local;
            }
        }

        /// <summary>
        /// Returns a random integer in the [0, Max) range.
        /// </summary>
        /// <returns>A random value in specified range.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Next(int max)
        {
            return Instance.Next(max);
        }

        /// <summary>
        /// Returns a random integer in the [Min, Max) range.
        /// </summary>
        /// <param name="min">Lower bound (inclusive).</param>
        /// <param name="max">Upper bound (exclusive).</param>
        /// <returns>A random value in specified range.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Next(int min, int max)
        {
            return Instance.Next(min, max);
        }

        /// <summary>
        /// Shuffles a list in-place using Fisher-Yates method.
        /// </summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="list">List to shuffle.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Instance.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
