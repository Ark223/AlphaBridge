using System;
using static AlphaBridge.Extensions;

namespace AlphaBridge
{
    /// <summary>
    /// Represents a state of the current trick.
    /// </summary>
    internal struct Trick
    {
        /// <summary>
        /// Cards currently played to the trick.
        /// </summary>
        public Card[] Cards;

        /// <summary>
        /// Player who led (started) this trick.
        /// </summary>
        public Player Leader;

        /// <summary>
        /// Number of cards played so far (0 to 4).
        /// </summary>
        public byte Count;

        /// <summary>
        /// Initializes a new trick with the specified leader.
        /// </summary>
        /// <param name="leader">Player who leads the trick.</param>
        public Trick(Player leader)
        {
            this.Cards = new Card[4];
            this.Leader = leader;
            this.Count = 0;
        }

        /// <summary>
        /// Creates a deep copy of the trick.
        /// </summary>
        /// <returns>A new <see cref="Trick"/> instance.</returns>
        public Trick Copy()
        {
            Trick copy = new Trick(this.Leader);
            Array.Copy(this.Cards, copy.Cards, this.Count);
            copy.Count = this.Count;
            return copy;
        }
    }
}
