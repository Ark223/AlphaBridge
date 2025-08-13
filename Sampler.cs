using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using static AlphaBridge.Extensions;

namespace AlphaBridge
{
    /// <summary>
    /// Supports generation and evaluation of random deals consistent with the current game state.
    /// </summary>
    internal sealed partial class Sampler
    {
        private readonly ulong[] _hands;
        private readonly ulong[] _plays;
        private readonly ushort _voids;
        private readonly Trick _trick;

        private byte[][] _known;
        private byte[] _lefts;
        private byte[] _needed;

        private Contract _contract;
        private ConstraintSet _constraints;
        private List<Card> _legal_moves;
        
        /// <summary>
        /// Initializes a new <see cref="Sampler"/> with hand distribution masks and trick state.
        /// </summary>
        /// <param name="hands">Array of bitmasks, one per player, indicating held cards.</param>
        /// <param name="plays">Array of bitmasks, one per player, indicating played cards.</param>
        /// <param name="hidden">Bitmask representing all cards not known to be in any hand.</param>
        /// <param name="voids">Bitmask indicating, for each player, which suits are voids.</param>
        /// <param name="trick">Current trick including played cards and leading player.</param>
        internal Sampler(ulong[] hands, ulong[] plays, ulong hidden, ushort voids, Trick trick)
        {
            this._hands = hands.ToArray();
            this._plays = plays.ToArray();
            this._voids = voids;
            this._trick = trick;
            this.UnplayTrick();
            this.Precompute(hidden);
        }
    }

    internal sealed partial class Sampler
    {
        /// <summary>
        /// Converts an array of card indices into a list of <see cref="Card"/> objects.
        /// </summary>
        /// <param name="hand">The hand as an array of card indices.</param>
        /// <returns>A new array containing the mapped cards.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        List<Card> Assign(in byte[] hand)
        {
            var cards = new List<Card>(hand.Length);
            for (int idx = 0; idx < hand.Length; idx++)
            {
                // Assign the card based on index
                cards[idx] = Game.Deck[hand[idx]];
            }
            return cards;
        }

        /// <summary>
        /// Converts a 52-bit bitmask into an array of card indexes (0–51).
        /// </summary>
        /// <param name="bitmask">Bitmask representing cards to be converted.</param>
        /// <returns>Array of card indexes extracted from the given bitmask.</returns>
        private byte[] Convert(ulong bitmask)
        {
            byte size = Utilities.PopCount(bitmask);
            List<byte> list = new List<byte>(size);
            while (bitmask != 0UL)
            {
                // Extract each set bit and store its index
                ulong bit = bitmask & (ulong)-(long)bitmask;
                list.Add(Utilities.TrailingZeroCount(bit));
                bitmask ^= bit;
            }
            return list.ToArray();
        }

        /// <summary>
        /// Precomputes baseline card distributions and missing card counts for each seat.
        /// </summary>
        /// <param name="hidden">
        /// Bitmask of all cards not yet known to be in any hand (hidden/unknown cards).
        /// </param>
        private void Precompute(ulong hidden)
        {
            this._needed = new byte[4];
            this._known = new byte[4][];
            for (int seat = 0; seat < 4; seat++)
            {
                // Combine held and played cards
                ulong assigned = this._hands[seat];
                assigned |= this._plays[seat];

                // Convert this mask to known cards
                var known = this.Convert(assigned);
                int count = known.Length;

                // Store cards and compute missing count
                this._needed[seat] = (byte)(13 - count);
                this._known[seat] = known;
            }

            // Convert hidden mask to unknown cards
            this._lefts = this.Convert(hidden);
        }

        /// <summary>
        /// Restores all cards from the current trick to the bitmasks.
        /// </summary>
        private void UnplayTrick()
        {
            Trick trick = this._trick;
            Player player = trick.Leader;
            for (int i = 0; i < trick.Count; i++)
            {
                ref Card card = ref trick.Cards[i];
                ulong bit = 1UL << card.Index();

                // Restore card to player's hand
                this._hands[(int)player] |= bit;

                // Remove card from played cards
                this._plays[(int)player] &= ~bit;

                // Advance to next player
                player = player.Next();
            }
        }

        /// <summary>
        /// Transforms the deal into PBN format, showing only unplayed cards for each player.
        /// </summary>
        /// <param name="deal">An array of four player hands representing the full deal.</param>
        /// <returns>A PBN string representing all hands after removing played cards.</returns>
        private string Transform(in List<Card>[] deal)
        {
            var hands = new string[4];
            for (int seat = 0; seat < 4; seat++)
            {
                var cards = new List<Card>[4];
                ref var played = ref this._plays[seat];

                // Initialize collector for each suit
                for (int suit = 0; suit < 4; suit++)
                    cards[suit] = new List<Card>();

                // Go through all original cards
                foreach (Card card in deal[seat])
                {
                    // Skip if this card was played
                    ulong bit = 1UL << card.Index();
                    if ((played & bit) != 0) continue;
                    cards[(int)card.Suit].Add(card);
                }

                // Prepare rank strings per suit
                string[] ranks = new string[4];
                for (int idx = 0; idx < 4; idx++)
                {
                    // Maintain PBN order (A-high)
                    Suit suit = PBN.PbnOrder[idx];
                    ref var list = ref cards[(int)suit];
                    list.Sort((a, b) => b.CompareTo(a));

                    // Convert sorted cards to rank characters
                    var temp = list.Select(c => c.ToString()[0]);
                    ranks[idx] = new string(temp.ToArray());
                }

                // Join suits as "S.H.D.C" for this seat
                hands[seat] = string.Join(".", ranks);
            }
            return string.Join(" ", hands);
        }
    }

    internal sealed partial class Sampler
    {
        /// <summary>
        /// Assigns a new contract, modifying the play conditions.
        /// </summary>
        /// <param name="contract">The contract to assign.</param>
        internal void SetContract(Contract contract)
        {
            this._contract = contract;
        }

        /// <summary>
        /// Stores constraints to be used for filtering generated deals.
        /// </summary>
        /// <param name="constraints">Deal filtering restrictions.</param>
        internal void SetConstraints(ConstraintSet constraints)
        {
            this._constraints = constraints;
        }

        /// <summary>
        /// Sets the array of legal moves available in the current context.
        /// </summary>
        /// <param name="moves">
        /// Array of <see cref="Card"/> objects representing all legal plays.
        /// </param>
        internal void SetLegalMoves(List<Card> moves)
        {
            this._legal_moves = moves;
        }

        /// <summary>
        /// Returns true if the specified deal meets all hand constraints.
        /// </summary>
        /// <param name="deal">An array of four player hands representing the full deal.</param>
        /// <returns>True if the deal satisfies all constraints; otherwise, false.</returns>
        internal bool Filter(in List<Card>[] deal)
        {
            for (int seat = 0; seat < 4; seat++)
            {
                Player player = (Player)seat;
                ref List<Card> hand = ref deal[seat];
                var checks = this._constraints[player];

                // Skip constraint check here
                if (!checks.Edited) continue;

                // Accumulate constaints
                int[] counts = new int[5];
                foreach (Card card in hand)
                {
                    counts[(int)card.Suit]++;
                    counts[4] += card.Hcp();
                }

                // Does the hand have required high card points?
                if (!checks.Hcp.Contains(counts[4])) return false;

                // Does the hand have required number of clubs?
                int clubs = counts.ElementAt((int)Suit.Clubs);
                if (!checks.Clubs.Contains(clubs)) return false;

                // Does the hand have required number of diamonds?
                int diamonds = counts.ElementAt((int)Suit.Diamonds);
                if (!checks.Diamonds.Contains(diamonds)) return false;

                // Does the hand have required number of hearts?
                int hearts = counts.ElementAt((int)Suit.Hearts);
                if (!checks.Hearts.Contains(hearts)) return false;

                // Does the hand have required number of spades?
                int spades = counts.ElementAt((int)Suit.Spades);
                if (!checks.Spades.Contains(spades)) return false;
            }
            return true;
        }

        /// <summary>
        /// Generate a random complete deal consistent with the current game state.
        /// </summary>
        /// <returns>Array of four player hands with assigned cards.</returns>
        internal List<Card>[] Generate()
        {
            // Copy remaining cards and shuffle
            var lefts = this.Assign(this._lefts);
            Random.Shuffle<Card>(lefts);

            // Initialize queue for dealing
            var pool = new Queue<Card>(lefts);
            var deal = new List<Card>[4];

            // True if seat is void in this suit
            bool voids(int seat, in Card card)
            {
                int bit = (seat * 4) + (int)card.Suit;
                return ((this._voids >> bit) & 1) != 0;
            }

            // Assign all cards to each player
            for (int seat = 0; seat < 4; seat++)
            {
                ref var hand = ref this._known[seat];
                deal[seat] = this.Assign(hand);

                // Fill hand until all hidden cards are assigned
                for (int need = this._needed[seat]; need-- > 0;)
                {
                    // Try each card in the pool at most once
                    for (int run = pool.Count; run-- > 0;)
                    {
                        // Draw random card from pool
                        Card card = pool.Dequeue();

                        // Check void restriction for this player
                        if (voids(seat, card)) pool.Enqueue(card);
                        else { deal[seat].Add(card); break; }
                    }
                }
            }
            return deal;
        }

        /// <summary>
        /// For each legal move, computes the double-dummy trick count from the given deal.
        /// </summary>
        /// <param name="deal">An array of four player hands representing the full deal.</param>
        /// <returns>Mapping from legal moves to tricks available after each play.</returns>
        internal Dictionary<Card, int> Solve(in List<Card>[] deal)
        {
            Player leader = this._trick.Leader;
            Suit strain = this._contract.Strain;
            var result = new Dictionary<Card, int>();

            // Build partial deal in a PBN format
            string hands = this.Transform(deal);

            // Collect all cards played so far in the current trick
            var played = this._trick.Cards.Select(c => c.ToString());
            string commands = string.Join(" ", played);

            // Set up a solver for the specified hands and strain
            using (var dds = new DDS("PBN", hands, strain, leader))
            {
                // Replay the current trick to update position
                if (commands != "") dds.Execute(commands);

                // Evaluate each move from this position
                foreach (Card card in this._legal_moves)
                {
                    // Calculate tricks after playing this card
                    result[card] = dds.Tricks(card.ToString());
                }
            }
            return result;
        }
    }
}
