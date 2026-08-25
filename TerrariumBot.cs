using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariumGladiators;

public readonly record struct BotMove(int PieceId, MovementPlane Plane, Int3 Target);

/// <summary>
/// A deliberately small two-ply opponent. It considers every movement plane,
/// resolves the real gravity rules for each branch, and then evaluates the
/// position after the opponent's best immediate reply.
/// </summary>
public sealed class TerrariumBot
{
    private const int MateScore = 1_000_000;

    private static readonly IReadOnlyDictionary<PieceKind, int> PieceValues =
        new Dictionary<PieceKind, int>
        {
            [PieceKind.Pawn] = 100,
            [PieceKind.Knight] = 320,
            [PieceKind.Bishop] = 330,
            [PieceKind.Trishop] = 360,
            [PieceKind.Rook] = 500,
            [PieceKind.Queen] = 900,
            [PieceKind.King] = 20_000
        };

    public void ScoutAndMark(TerrariumModel position)
    {
        if (!position.MinesweeperEnabled || position.Winner is not null) return;

        var originalSelection = position.SelectedId;
        var side = position.Turn;
        var scans = 0;

        // Scouting is plane-agnostic: one use of a pattern covers its range-one
        // neighborhood across XY, XZ, and YZ.
        foreach (var piece in position.Pieces.Where(piece => piece.Side == side).ToList())
        {
            if (!position.Select(piece.Id)) continue;
            foreach (var pattern in position.ScoutPatterns(piece).ToList())
                if (position.ScoutForBot(pattern)) scans++;
        }

        DeduceMineKnowledge(position);
        if (originalSelection is { } selected && position.Select(selected)) { }
        else position.ClearSelection();
        position.SetMessage(
            $"{side} bot used {scans} free scans and now marks " +
            $"{position.BotSafeMarks.Count} clear / {position.BotMineFlags.Count} mined cells.");
    }

    public BotMove? ChooseMove(TerrariumModel position)
    {
        if (position.Winner is not null) return null;

        // Search only on clones so move generation cannot alter the displayed
        // movement plane, selection, message, or undo history.
        // The search receives only mines established by scouting or visible
        // clue deduction. The hidden mine array is deliberately discarded.
        var root = position.CloneForBotSearch();
        var botSide = root.Turn;
        var opponent = Other(botSide);
        var moves = GenerateMoves(root, botSide);
        if (moves.Count == 0) return null;

        var branches = new List<(BotMove Move, TerrariumModel Position)>(moves.Count);
        foreach (var move in moves)
        {
            var afterMove = ApplyMove(root, move);
            if (afterMove is null) continue;

            // Always take mate in one, whether it is a direct capture or a
            // king destroyed by the resulting terrain/gravity cascade.
            if (afterMove.Winner == botSide) return move;
            branches.Add((move, afterMove));
        }

        // Root branches share no mutable state, so reply searches can use the
        // available CPU cores without complicating the model itself.
        var analyses = branches.AsParallel()
            .WithDegreeOfParallelism(Math.Max(1, Environment.ProcessorCount))
            .Select(branch =>
            {
                var reply = EvaluateOpponentReplies(root, branch.Position, botSide, opponent);
                return new MoveAnalysis(branch.Move, reply.Score, reply.CanMateInOne);
            })
            .ToList();

        if (analyses.Count == 0) return null;

        // If at least one move prevents mate in one, never choose a move that
        // permits it. If all moves lose, retain the shallow search's best try.
        var safe = analyses.Where(analysis => !analysis.AllowsMateInOne).ToList();
        var candidates = safe.Count > 0 ? safe : analyses;
        return candidates
            .OrderByDescending(analysis => analysis.Score)
            .ThenBy(analysis => analysis.Move.PieceId)
            .ThenBy(analysis => analysis.Move.Plane)
            .ThenBy(analysis => analysis.Move.Target.X)
            .ThenBy(analysis => analysis.Move.Target.Y)
            .ThenBy(analysis => analysis.Move.Target.Z)
            .First().Move;
    }

    private static void DeduceMineKnowledge(TerrariumModel position)
    {
        position.BotMineFlags.RemoveWhere(cell =>
            !TerrariumModel.IsInside(cell) || !position.IsSolid(cell) || cell.Z >= 7);
        position.BotSafeMarks.RemoveWhere(cell =>
            !TerrariumModel.IsInside(cell) || !position.IsSolid(cell));

        var knownMines = new HashSet<Int3>(position.BotMineFlags);
        var knownSafe = new HashSet<Int3>(position.BotSafeMarks);
        knownSafe.UnionWith(position.RevealedClues.Where(TerrariumModel.IsInside));

        var changed = true;
        while (changed)
        {
            changed = false;
            var constraints = VisibleConstraints(position, knownSafe, knownMines);
            foreach (var constraint in constraints)
            {
                if (constraint.Remaining == 0)
                    foreach (var cell in constraint.Cells)
                        changed |= knownSafe.Add(cell);
                else if (constraint.Remaining == constraint.Cells.Count)
                    foreach (var cell in constraint.Cells)
                        changed |= knownMines.Add(cell);
            }

            if (changed) continue;
            for (var i = 0; i < constraints.Count && !changed; i++)
            for (var j = 0; j < constraints.Count && !changed; j++)
            {
                if (i == j || constraints[i].Cells.Count >= constraints[j].Cells.Count ||
                    !constraints[i].Cells.IsSubsetOf(constraints[j].Cells)) continue;
                var difference = new HashSet<Int3>(constraints[j].Cells);
                difference.ExceptWith(constraints[i].Cells);
                var remaining = constraints[j].Remaining - constraints[i].Remaining;
                if (remaining == 0)
                    foreach (var cell in difference) changed |= knownSafe.Add(cell);
                else if (remaining == difference.Count)
                    foreach (var cell in difference) changed |= knownMines.Add(cell);
            }
        }

        knownSafe.ExceptWith(knownMines);
        position.BotMineFlags.UnionWith(knownMines);
        position.BotSafeMarks.UnionWith(knownSafe.Where(position.IsSolid));
        position.BotSafeMarks.ExceptWith(position.BotMineFlags);
    }

    private static List<ClueConstraint> VisibleConstraints(
        TerrariumModel position, HashSet<Int3> knownSafe, HashSet<Int3> knownMines)
    {
        var constraints = new List<ClueConstraint>();
        foreach (var clueCell in position.RevealedClues)
        {
            if (position.ClueAt(clueCell) is not { } clue) continue;
            var cells = new HashSet<Int3>();
            var adjacentKnownMines = 0;
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                var cell = clueCell + new Int3(dx, dy, dz);
                if (!TerrariumModel.IsInside(cell) || cell.Z >= 7 || !position.IsSolid(cell)) continue;
                if (knownMines.Contains(cell)) adjacentKnownMines++;
                else if (!knownSafe.Contains(cell)) cells.Add(cell);
            }
            var remaining = clue - adjacentKnownMines;
            if (cells.Count > 0 && remaining >= 0 && remaining <= cells.Count)
                constraints.Add(new ClueConstraint(cells, remaining));
        }
        return constraints;
    }

    private static ReplyAnalysis EvaluateOpponentReplies(
        TerrariumModel root, TerrariumModel position, Side botSide, Side opponent)
    {
        var replies = GenerateMoves(position, opponent);
        if (replies.Count == 0)
            return new ReplyAnalysis(
                Evaluate(position, botSide) - PreservationPenalty(root, position, botSide), false);

        var worstScore = int.MaxValue;
        var canMateInOne = false;
        foreach (var reply in replies)
        {
            var afterReply = ApplyMove(position, reply);
            if (afterReply is null) continue;

            // Material lost beyond what the opponent lost is penalized again.
            // This makes the bot actively rescue attacked pieces and decline
            // exchanges where it gives up the more valuable side of the trade.
            var score = Evaluate(afterReply, botSide) -
                        PreservationPenalty(root, afterReply, botSide);
            if (afterReply.Winner == opponent)
            {
                canMateInOne = true;
                score = -MateScore;
            }
            worstScore = Math.Min(worstScore, score);
        }

        return new ReplyAnalysis(
            worstScore == int.MaxValue
                ? Evaluate(position, botSide) - PreservationPenalty(root, position, botSide)
                : worstScore,
            canMateInOne);
    }

    private static int PreservationPenalty(
        TerrariumModel root, TerrariumModel position, Side botSide)
    {
        var opponent = Other(botSide);
        var ownLoss = Material(root, botSide) - Material(position, botSide);
        var opponentLoss = Material(root, opponent) - Material(position, opponent);
        return Math.Max(0, ownLoss - opponentLoss) * 3;
    }

    private static int Material(TerrariumModel position, Side side) =>
        position.Pieces.Where(piece => piece.Side == side)
            .Sum(piece => PieceValues[piece.Kind]);

    private static int Evaluate(TerrariumModel position, Side botSide)
    {
        if (position.Winner == botSide) return MateScore;
        if (position.Winner == Other(botSide)) return -MateScore;

        var score = 0;
        foreach (var piece in position.Pieces)
        {
            var sign = piece.Side == botSide ? 1 : -1;
            var material = PieceValues[piece.Kind];

            // Small positional terms break material ties: central pieces have
            // more plane options, and advanced pawns are closer to promotion.
            var center = 14 - Math.Abs(piece.Position.X * 2 - 7)
                            - Math.Abs(piece.Position.Y * 2 - 7);
            var advancement = piece.Kind == PieceKind.Pawn
                ? piece.Side == Side.White ? piece.Position.Y : 7 - piece.Position.Y
                : 0;
            var activity = piece.Kind == PieceKind.King ? center : center * 2;
            score += sign * (material + activity + advancement * 6);
        }
        return score;
    }

    private static List<BotMove> GenerateMoves(TerrariumModel position, Side side)
    {
        var moves = new List<BotMove>();
        foreach (var plane in Enum.GetValues<MovementPlane>())
        {
            position.SetPlane(plane);
            foreach (var piece in position.Pieces.Where(piece => piece.Side == side).ToList())
            {
                moves.AddRange(position.LegalMoves(piece)
                    .Select(target => new BotMove(piece.Id, plane, target)));
            }
        }
        return moves;
    }

    private static TerrariumModel? ApplyMove(TerrariumModel position, BotMove move)
    {
        var result = position.CloneForSimulation();
        result.SetPlane(move.Plane);
        if (!result.Select(move.PieceId) || !result.TryMove(move.Target)) return null;
        if (result.PendingPromotionPieceId is not null) result.Promote(PieceKind.Queen);
        return result;
    }

    private static Side Other(Side side) => side == Side.White ? Side.Black : Side.White;

    private readonly record struct MoveAnalysis(BotMove Move, int Score, bool AllowsMateInOne);
    private readonly record struct ReplyAnalysis(int Score, bool CanMateInOne);
    private readonly record struct ClueConstraint(HashSet<Int3> Cells, int Remaining);
}
