using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AlphaBridge
{
    /// <summary>
    /// General-purpose utility functions for bridge logic and combinatorics.
    /// </summary>
    internal class Utilities
    {
        /// <summary>
        /// Global random number generator for all sampling and shuffling operations.
        /// </summary>
        private static readonly Random Randomizer = new Random();

        /// <summary>
        /// Computes the binomial coefficient C(n, k) = n! / (k! * (n - k)!).
        /// </summary>
        /// <param name="n">The size of the total set.</param>
        /// <param name="k">The number of elements to choose.</param>
        /// <returns>The binomial coefficient ("n choose k").</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Permutations(int n, int k)
        {
            int result = 1;
            if (k > n - k) k = n - k;
            for (int i = 1; i <= k; i++)
            {
                result *= n - k + i;
                result /= i;
            }
            return result;
        }

        /// <summary>
        /// Counts the number of bits set to 1 in the given value.
        /// </summary>
        /// <param name="value">The 64-bit integer to count bits in.</param>
        /// <returns>The number of set bits in <paramref name="value"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte PopCount(ulong value)
        {
            byte result = 0;
            while (value != 0)
            {
                value &= value - 1;
                result++;
            }
            return result;
        }

        /// <summary>
        /// Counts how many low-order zero bits precede the first one-bit.
        /// </summary>
        /// <param name="value">The value to scan (must be nonzero).</param>
        /// <returns>
        /// The position of the least significant one-bit in <paramref name="value"/>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte TrailingZeroCount(ulong value)
        {
            if (value == 0)
            {
                return 64;
            }
            byte result = 0;
            while ((value & 1UL) == 0)
            {
                result++;
                value >>= 1;
            }
            return result;
        }
    }
}
