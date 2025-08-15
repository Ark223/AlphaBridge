using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AlphaBridge.Extensions;

namespace AlphaBridge
{
    /// <summary>
    /// Options for initializing a <see cref="Game"/>. Includes the deal, dealer, contract, and optional constraints.
    /// </summary>
    public sealed class GameOptions
    {
        /// <summary>
        /// Deal in PBN format (use "..." for unknown hands).
        /// </summary>
        public string Deal { get; set; } = string.Empty;

        /// <summary>
        /// The leading player (North, East, South, or West).
        /// </summary>
        public Player Leader { get; set; } = Player.North;

        /// <summary>
        /// The contract to be played (e.g., 3NT, 4H, ...).
        /// </summary>
        public Contract Contract { get; set; } = Contract.None;

        /// <summary>
        /// Optional per-player hand constraints (for filtering playouts).
        /// </summary>
        public ConstraintSet Constraints { get; set; } = ConstraintSet.Empty;
    }

    /// <summary>
    /// A public interface for a bridge game, separating API from implementation.
    /// </summary>
    internal interface IGame
    {
        /// <summary>
        /// Attempts to play a move specified as a string (e.g., "AS" for Ace of Spades).
        /// </summary>
        /// <param name="move">String representation of the card played.</param>
        /// <param name="check">If true, validates the move before playing.</param>
        /// <returns>True if move is legal and accepted; otherwise, false.</returns>
        bool Play(string move, bool check = true);

        /// <summary>
        /// Attempts to play a move specified as a <see cref="Card"/> object.
        /// </summary>
        /// <param name="card">The card played.</param>
        /// <param name="check">If true, validates the move before playing.</param>
        /// <returns>True if move is legal and accepted; otherwise, false.</returns>
        bool Play(in Card card, bool check = true);

        /// <summary>
        /// Returns a list of all pseudo-legal moves available to the current player.
        /// </summary>
        /// <returns>
        /// A list of <see cref="Card"/> objects representing all available plays.
        /// </returns>
        List<Card> GetMoves();

        /// <summary>
        /// Checks whether a move specified as a string (e.g., "AS") is legal in the current game state.
        /// </summary>
        /// <param name="move">String representation of the card.</param>
        /// <returns>True if the move is legal; otherwise, false.</returns>
        bool IsLegal(string move);

        /// <summary>
        /// Checks whether a move specified as a <see cref="Card"/> is legal in the current game state.
        /// </summary>
        /// <param name="card">Card to be played.</param>
        /// <returns>True if the move is legal; otherwise, false.</returns>
        bool IsLegal(in Card card);

        /// <summary>
        /// Determines whether the game has ended (all tricks played).
        /// </summary>
        /// <returns>True if the game is finished; otherwise, false.</returns>
        bool IsOver();

        /// <summary>
        /// Undoes the last move if possible.
        /// </summary>
        /// <returns>True if an undo was performed; otherwise, false.</returns>
        bool Undo();

        /// <summary>
        /// Redoes the next move if possible (after an undo).
        /// </summary>
        /// <returns>True if a redo was performed; otherwise, false.</returns>
        bool Redo();

        /// <summary>
        /// Creates a deep, independent copy of the current game state.
        /// </summary>
        /// <returns>A new <see cref="Game"/> instance with identical state.</returns>
        Game Clone();
    }

    /// <summary>
    /// Bridge game public state and move history. Unknown hands are allowed ("-" in PBN).
    /// </summary>
    public sealed partial class Game : IGame, IDisposable
    {
        private ulong[] _hands;
        private ulong[] _plays;
        private byte[] _lefts;
        private ulong _hidden;
        private ushort _voids;

        private Trick _trick;
        private Player _leader;
        private byte _ns_tricks;
        private byte _ew_tricks;

        private readonly Contract _contract;
        private readonly ConstraintSet _constraints;

        /// <summary>
        /// Undo stack for move history (supports undo operations).
        /// </summary>
        private Stack<History> _undo = new Stack<History>();

        /// <summary>
        /// Redo stack for move history (supports redo operations).
        /// </summary>
        private Stack<History> _redo = new Stack<History>();

        /// <summary>
        /// Represents a snapshot of game state for undo/redo.
        /// </summary>
        private struct History
        {
            internal Card Card;
            internal Trick Trick;
            internal Player Player;
            internal ulong[] Hands;
            internal ulong[] Plays;
            internal byte[] Lefts;
            internal ulong Hidden;
            internal ushort Voids;
        }
    }

    public sealed partial class Game : IGame, IDisposable
    {
        /// <summary>
        /// A precomputed array containing all 52 cards in the deck.
        /// </summary>
        public static readonly Card[] Deck = Array.ConvertAll
        (
            Enumerable.Range(0, 52).ToArray(), Card.Create
        );

        /// <summary>
        /// Returns a bitmask of all cards currently held by any player.
        /// </summary>
        /// <returns>
        /// A 52-bit mask where each set bit indicates a card held in some hand.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong AllHeld()
        {
            return this._hands[0] | this._hands[1] | this._hands[2] | this._hands[3];
        }

        /// <summary>
        /// Returns a bitmask of all cards played by any player.
        /// </summary>
        /// <returns>
        /// A 52-bit mask with bits set for every card that has been played so far.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong AllPlayed()
        {
            return this._plays[0] | this._plays[1] | this._plays[2] | this._plays[3];
        }

        /// <summary>
        /// Returns a bitmask of all hidden or unknown cards (not held and not played).
        /// </summary>
        /// <returns>
        /// A 52-bit mask with bits set for the cards not in any hand or played.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong HiddenSet()
        {
            return (~this.AllHeld()) & ((1UL << 52) - 1UL);
        }

        /// <summary>
        /// Marks the specified suit as void for the leading player.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetVoid(Suit suit)
        {
            this._voids |= (ushort)(1 << ((int)this._leader * 4 + (int)suit));
        }

        /// <summary>
        /// Returns a bitmask covering all 13 cards of specified suit within a deck.
        /// </summary>
        /// <param name="suit">Suit for which to get the mask.</param>
        /// <returns>
        /// A 52-bit mask with 13 bits corresponding to the specified suit.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong SuitMask(Suit suit)
        {
            return 0x1FFFUL << ((int)suit * 13); // 0x1FFF = 13 bits set to 1
        }

        /// <summary>
        /// Determines the suit led for the current trick.
        /// </summary>
        /// <returns>The suit led in this trick.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Suit FirstLead()
        {
            bool played = (this._trick.Count > 0);
            return played ? this._trick.Cards[0].Suit : Suit.NoTrump;
        }

        /// <summary>
        /// Determines the suit led for the current trick.
        /// </summary>
        /// <param name="card">Card being played.</param>
        /// <returns>The suit led in this trick.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Suit FirstLead(in Card card)
        {
            bool played = (this._trick.Count > 0);
            return played ? this._trick.Cards[0].Suit : card.Suit;
        }

        /// <summary>
        /// Returns the number of unknown cards remaining in each player's hand.
        /// </summary>
        /// <returns>
        /// A sequence where each value is the count of unknown cards for that player.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] FindLefts()
        {
            return Array.ConvertAll(new int[] {0, 1, 2, 3}, seat =>
                (byte)(13 - Utilities.PopCount(this._hands[seat])));
        }

        /// <summary>
        /// Returns the trick-taking priority of a card: trump (2), led suit (1), or other (0).
        /// </summary>
        /// <param name="card">Card to evaluate.</param>
        /// <param name="trump">Trump suit for this contract.</param>
        /// <param name="lead">The suit led for this trick.</param>
        /// <returns>2 for trump, 1 for led suit, 0 otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Priority(in Card card, Suit trump, Suit lead)
        {
            return trump != Suit.NoTrump && card.Suit ==
                trump ? 2 : card.Suit == lead ? 1 : 0;
        }
    }

    public sealed partial class Game : IGame, IDisposable
    {
        /// <summary>
        /// Marks the leader's hand as void in the given suit, then assigns all hidden
        /// <br></br>cards of that suit to the only other hand that can still hold them.
        /// </summary>
        /// <param name="suit">Suit in which the leader is void.</param>
        private void ApplyVoid(Suit suit)
        {
            // Mark the void
            this.SetVoid(suit);

            // Compute bitmask of hidden cards of that suit
            ulong hidden = this._hidden & this.SuitMask(suit);
            if (hidden == 0UL) return;

            // Find the non-leading player with any unknown cards
            var targets = Enumerable.Range(0, 4).Where<int>(seat =>
                seat != (int)this._leader && this._lefts[seat] > 0);

            // Only proceed if exactly one candidate remains
            int target = targets.DefaultIfEmpty(-1).First();
            if (target == -1 || targets.Count() > 1) return;

            // Assign hidden cards to the target hand
            byte count = Utilities.PopCount(hidden);
            this._hands[target] |= hidden;
            this._lefts[target] -= count;
            this._hidden &= ~hidden;
        }

        /// <summary>
        /// Determines the winner of a current trick and updates the game state.
        /// </summary>
        private void FinishTrick()
        {
            Suit trump = this._contract.Strain;
            Suit lead = this._trick.Cards[0].Suit;

            // Find the index of the winning card for this trick
            var winner = Enumerable.Range(0, 4).OrderBy(index =>
            {
                ref Card card = ref this._trick.Cards[index];
                int priority = -this.Priority(card, trump, lead);
                return (Priority: priority, CardRank: -card.Rank);
            })
            .First();

            // Determine the player who won the current trick
            var player = this._trick.Leader.Advance(winner);

            // Update trick counts for pairs
            int ew_side = (int)(player) & 1;
            this._ew_tricks += (byte)(ew_side);
            this._ns_tricks += (byte)(1 - ew_side);

            // Update and prepare next trick
            this._trick = new Trick(player);
            this._leader = player;
        }
    }

    public sealed partial class Game : IGame, IDisposable
    {
        /// <summary>
        /// Initializes a new bridge game with the specified options.
        /// </summary>
        /// <param name="options">Game options, including deal and contract.</param>
        public Game(GameOptions options)
        {
            this._leader = options.Leader;
            this._contract = options.Contract;
            this._constraints = options.Constraints;
            this._hands = PBN.ParseDeal(options.Deal);

            this._trick = new Trick(this._leader);
            this._hidden = this.HiddenSet();
            this._lefts = this.FindLefts();
            this._plays = new ulong[4];

            this._ns_tricks = 0;
            this._ew_tricks = 0;
        }

        /// <summary>
        /// Factory method for creating a new <see cref="Game"/> instance.
        /// </summary>
        /// <param name="options">Game options, including deal and contract.</param>
        /// <returns>A new <see cref="Game"/> instance.</returns>
        public static Game New(GameOptions options)
        {
            return new Game(options);
        }

        /// <summary>
        /// Attempts to play a move specified as a string (e.g., "AS" for Ace of Spades).
        /// </summary>
        /// <param name="move">String representation of the card played.</param>
        /// <param name="check">If true, validates the move before playing.</param>
        /// <returns>True if move is legal and accepted; otherwise, false.</returns>
        public bool Play(string move, bool check = true)
        {
            bool parsed = Card.TryParse(move, out Card card);
            return parsed && this.Play(card, check);
        }

        /// <summary>
        /// Attempts to play a move specified as a <see cref="Card"/> object.
        /// </summary>
        /// <param name="card">The card played.</param>
        /// <param name="check">If true, validates the move before playing.</param>
        /// <returns>True if move is legal and accepted; otherwise, false.</returns>
        public bool Play(in Card card, bool check = true)
        {
            // Check if the move is legal according to rules
            if (check && !this.IsLegal(card)) return false;

            // Determine lead suit for this trick
            Suit lead = this.FirstLead(card);

            // Compute bitmask for played card
            ulong bit = 1UL << card.Index();

            // Save the current game state
            this._undo.Push(new History
            {
                Card = card,
                Player = this._leader,
                Trick = this._trick.Copy(),
                Hands = this._hands.ToArray(),
                Plays = this._plays.ToArray(),
                Lefts = this._lefts.ToArray(),
                Hidden = this._hidden,
                Voids = this._voids
            });
            this._redo.Clear();

            // Record a void if didn't follow the suit
            if (card.Suit != lead) this.ApplyVoid(lead);

            // Remove hidden card and consume an unknown slot
            if ((this._hands[(int)this._leader] & bit) == 0)
            {
                this._hidden &= ~bit;
                this._lefts[(int)this._leader]--;
            }

            // Remove the card from the player’s hand
            this._hands[(int)this._leader] &= ~bit;

            // Add this card to the trick and mark as played
            this._trick.Cards[this._trick.Count++] = card;
            this._plays[(int)this._leader] |= bit;

            // Determine winner or pass lead to next player
            if (this._trick.Count == 4) this.FinishTrick();
            else this._leader = this._leader.Next();

            return true;
        }

        /// <summary>
        /// Returns a list of all pseudo-legal moves available to the current player.
        /// </summary>
        /// <returns>
        /// A list of <see cref="Card"/> objects representing all available plays.
        /// </returns>
        public List<Card> GetMoves()
        {
            var moves = new List<Card>();
            Suit lead = this.FirstLead();

            // Get the current leader’s known hand mask
            ulong hand = this._hands[(int)this._leader];

            // Get cards still available to play
            ulong unplayed = ~this.AllPlayed();

            // Collect candidates for this player
            ulong available = unplayed & hand;

            // Add possible cards from hidden pool
            if (this._lefts[(int)this._leader] > 0)
                available |= unplayed & this._hidden;

            // Does the leader have any cards of the lead suit?
            bool has_lead = (hand & this.SuitMask(lead)) != 0;

            // Determine if the player must follow the suit led
            bool must_follow = this._trick.Count > 0 && has_lead;

            // If must follow suit, restrict cards to that suit
            if (must_follow) available &= this.SuitMask(lead);

            while (available != 0)
            {
                // Get the card and remove it from candidate set
                ulong bit = available & (ulong)-(long)available;
                available ^= bit;

                // Calculate the card's index and retrieve it
                byte index = Utilities.TrailingZeroCount(bit);
                ref readonly Card card = ref Game.Deck[index];

                // Can't play that suit that's already marked as a void
                int void_bit = ((int)this._leader * 4) + (int)card.Suit;
                if (((this._voids >> void_bit) & 1) != 0) continue;

                // Checks passed
                moves.Add(card);
            }

            return moves;
        }

        /// <summary>
        /// Checks whether a move specified as a string (e.g., "AS") is legal in the current game state.
        /// </summary>
        /// <param name="move">String representation of the card.</param>
        /// <returns>True if the move is legal; otherwise, false.</returns>
        public bool IsLegal(string move)
        {
            bool parsed = Card.TryParse(move, out Card card);
            return parsed && this.IsLegal(card);
        }

        /// <summary>
        /// Checks whether a move specified as a <see cref="Card"/> is legal in the current game state.
        /// </summary>
        /// <param name="card">Card to be played.</param>
        /// <returns>True if the move is legal; otherwise, false.</returns>
        public bool IsLegal(in Card card)
        {
            // Compute bitmask for played card
            ulong bit = 1UL << card.Index();

            // Determine lead suit for the trick
            Suit lead = this.FirstLead(card);

            // Get the current leader’s known hand mask
            ulong hand = this._hands[(int)this._leader];

            // Does the leader have any cards of the lead suit?
            bool has_lead = (hand & this.SuitMask(lead)) != 0;

            // Can't discard off-suit if still has the suit led
            if (has_lead && card.Suit != lead) return false;

            // Does the leader actually hold this card?
            bool has_card = (hand & bit) != 0;

            // Is the card present in the hidden pool?
            bool hidden = (this._hidden & bit) != 0;

            // Check if player has exhausted all hidden cards 
            bool lefts = this._lefts[(int)this._leader] > 0;

            // Card must be in the hand or be in a hidden pool
            if (!has_card && (!hidden || !lefts)) return false;

            // Return if the exact card was already played
            if ((this.AllPlayed() & bit) != 0) return false;

            // Can't play that suit that's already marked as a void
            int void_bit = ((int)this._leader * 4) + (int)card.Suit;
            if (((this._voids >> void_bit) & 1) != 0) return false;

            return true;
        }

        /// <summary>
        /// Determines whether the game has ended (all tricks played).
        /// </summary>
        /// <returns>True if the game is finished; otherwise, false.</returns>
        public bool IsOver()
        {
            return (this._ns_tricks + this._ew_tricks) >= 13;
        }

        /// <summary>
        /// Undoes the last move if possible.
        /// </summary>
        /// <returns>True if an undo was performed; otherwise, false.</returns>
        public bool Undo()
        {
            // Cannot undo; no previous moves
            if (!this._undo.Any()) return false;

            // Save current state to redo
            this._redo.Push(new History
            {
                Card = default,
                Player = this._leader,
                Trick = this._trick.Copy(),
                Hands = this._hands.ToArray(),
                Plays = this._plays.ToArray(),
                Lefts = this._lefts.ToArray(),
                Hidden = this._hidden,
                Voids = this._voids
            });

            // Restore state from undo
            var hist = this._undo.Pop();

            this._leader = hist.Player;
            this._trick = hist.Trick.Copy();
            this._hands = hist.Hands.ToArray();
            this._plays = hist.Plays.ToArray();
            this._lefts = hist.Lefts.ToArray();
            this._hidden = hist.Hidden;
            this._voids = hist.Voids;

            return true;
        }

        /// <summary>
        /// Redoes the next move if possible (after an undo).
        /// </summary>
        /// <returns>True if a redo was performed; otherwise, false.</returns>
        public bool Redo()
        {
            // Cannot redo; no further moves
            if (!this._redo.Any()) return false;

            // Save current state to undo
            this._undo.Push(new History
            {
                Card = default,
                Player = this._leader,
                Trick = this._trick.Copy(),
                Hands = this._hands.ToArray(),
                Plays = this._plays.ToArray(),
                Lefts = this._lefts.ToArray(),
                Hidden = this._hidden,
                Voids = this._voids
            });

            // Restore state from redo
            var hist = this._redo.Pop();

            this._leader = hist.Player;
            this._trick = hist.Trick.Copy();
            this._hands = hist.Hands.ToArray();
            this._plays = hist.Plays.ToArray();
            this._lefts = hist.Lefts.ToArray();
            this._hidden = hist.Hidden;
            this._voids = hist.Voids;

            return true;
        }

        /// <summary>
        /// Creates a deep, independent copy of the current game state.
        /// </summary>
        /// <returns>A new <see cref="Game"/> instance with identical state.</returns>
        public Game Clone()
        {
            // Shallow copy the game state instance
            var copy = (Game)this.MemberwiseClone();

            // Deep copy trick state and hands
            copy._trick = this._trick.Copy();
            copy._hands = this._hands.ToArray();
            copy._plays = this._plays.ToArray();
            copy._lefts = this._lefts.ToArray();

            // Clone undo and redo history stacks
            copy._undo = new Stack<History>(this._undo);
            copy._redo = new Stack<History>(this._redo);
            return copy;
        }

        /// <summary>
        /// Creates a <see cref="Sampler"/> instance initialized with the current game state.
        /// </summary>
        /// <returns>
        /// A new <see cref="Sampler"/> object that can generate and evaluate random deals.
        /// </returns>
        internal Sampler Sampling()
        {
            Sampler sampler =
                new Sampler(this._hands, this._plays,
                this._hidden, this._voids, this._trick);

            // Apply constraints, contract, and moves
            sampler.SetConstraints(this._constraints);
            sampler.SetContract(this._contract);
            sampler.SetLegalMoves(this.GetMoves());
            return sampler;
        }

        /// <summary>
        /// A human-readable string summary of the current game state.
        /// </summary>
        /// <returns>A string describing the game state.</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Leader: {this._leader}");
            sb.AppendLine($"Contract: {this._contract}");
            sb.AppendLine("Hands:");

            // For each player, list all cards currently held in hand
            foreach (Player player in Enum.GetValues(typeof(Player)))
            {
                ulong mask = this._hands[(int)player];
                var cards = Enumerable.Range(0, 52)
                    .Where(index => (mask & (1UL << index)) != 0)
                    .Select(index => Game.Deck[index].ToString());
                sb.AppendLine($" {player}: {string.Join(" ", cards)}");
            }

            // Display cards currently on the table for this trick
            var played = this._trick.Cards.Take(this._trick.Count);
            var trick = string.Join(" ", played.Select(c => c.ToString()));
            sb.AppendLine($"Current Trick ({this._trick.Count}): {trick}");
            return sb.ToString();
        }

        /// <summary>
        /// Finalizer for <see cref="Game"/> instance.
        /// </summary>
        ~Game() => this.Release();

        /// <summary>
        /// Clears internal undo and redo stacks.
        /// </summary>
        private void Release()
        {
            this._undo.Clear();
            this._redo.Clear();
        }

        /// <summary>
        /// Releases all resources used by the <see cref="Game"/> instance.
        /// </summary>
        public void Dispose()
        {
            this.Release();
            GC.SuppressFinalize(this);
        }
    }
}
