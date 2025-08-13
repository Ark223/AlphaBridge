using System;
using System.Runtime.InteropServices;
using static AlphaBridge.Extensions;

namespace AlphaBridge
{
    /// <summary>
    /// Managed wrapper for the Bridge Calculator double‑dummy solver.
    /// </summary>
    internal sealed class DDS : IDisposable
    {
        private IntPtr instance = IntPtr.Zero;

        /// <summary>
        /// Static P/Invoke definitions for the native solver DLL.
        /// </summary>
        private static class Solver
        {
            [DllImport("libcalcdds", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            internal static extern IntPtr bcalcDDS_new(string format, string hands, int strain, int leader);

            [DllImport("libcalcdds", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr bcalcDDS_clone(IntPtr solver);

            [DllImport("libcalcdds", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void bcalcDDS_delete(IntPtr solver);

            [DllImport("libcalcdds", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            internal static extern void bcalcDDS_exec(IntPtr solver, string commands);

            [DllImport("libcalcdds", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int bcalcDDS_getTricksToTake(IntPtr solver);

            [DllImport("libcalcdds", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            internal static extern int bcalcDDS_getTricksToTakeEx(IntPtr solver, int tricks, string card);
        }

        /// <summary>
        /// Creates a DDS wrapper for an existing unmanaged solver pointer.
        /// </summary>
        /// <param name="pointer">Native pointer to DDS instance.</param>
        internal DDS(IntPtr pointer)
        {
            this.instance = pointer;
        }

        /// <summary>
        /// Creates a new double-dummy solver for the specified deal.
        /// </summary>
        /// <param name="format">Format string (e.g., "PBN").</param>
        /// <param name="hands">String representation of the hands.</param>
        /// <param name="strain">Contract strain (suit or NT).</param>
        /// <param name="leader">Player to lead.</param>
        internal DDS(string format, string hands, Suit strain, Player leader)
        {
            this.instance = Solver.bcalcDDS_new(format, hands, (int)strain, (int)leader);
        }

        /// <summary>
        /// Sends commands to the solver for execution.
        /// </summary>
        /// <param name="commands">Command string for the native solver.</param>
        internal void Execute(string commands)
        {
            Solver.bcalcDDS_exec(this.instance, commands);
        }

        /// <summary>
        /// Gets the maximum tricks to take after playing a specific card.
        /// </summary>
        /// <param name="card">Card played as a string (e.g., "AS").</param>
        /// <returns>Number of tricks to take from that state.</returns>
        internal int Tricks(string card)
        {
            return Solver.bcalcDDS_getTricksToTakeEx(this.instance, -1, card);
        }

        /// <summary>
        /// Finalizer for <see cref="DDS"/> instance.
        /// </summary>
        ~DDS() => this.Release();

        /// <summary>
        /// Frees the native solver instance, if allocated.
        /// </summary>
        private void Release()
        {
            if (this.instance != IntPtr.Zero)
            {
                Solver.bcalcDDS_delete(this.instance);
                this.instance = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Disposes the DDS wrapper and releases unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            this.Release();
            GC.SuppressFinalize(this);
        }
    }
}
