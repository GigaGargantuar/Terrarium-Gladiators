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
            [PieceKind.Rook] = 500,
            [PieceKind.Queen] = 900,
            [PieceKind.King] = 20_000
        };

    public BotMove? ChooseMove(TerrariumModel position)
    {
        if (position.Winner is not null) return null;

        // Search only on clones so move generation cannot alter the displayed
        // movement plane, selection, message, or undo history.
        var root = position.CloneForSimulation();
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
                var reply = EvaluateOpponentReplies(branch.Position, botSide, opponent);
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

    private static ReplyAnalysis EvaluateOpponentReplies(
        TerrariumModel position, Side botSide, Side opponent)
    {
        var replies = GenerateMoves(position, opponent);
        if (replies.Count == 0)
            return new ReplyAnalysis(Evaluate(position, botSide), false);

        var worstScore = int.MaxValue;
        var canMateInOne = false;
        foreach (var reply in replies)
        {
            var afterReply = ApplyMove(position, reply);
            if (afterReply is null) continue;

            var score = Evaluate(afterReply, botSide);
            if (afterReply.Winner == opponent)
            {
                canMateInOne = true;
                score = -MateScore;
            }
            worstScore = Math.Min(worstScore, score);
        }

        return new ReplyAnalysis(
            worstScore == int.MaxValue ? Evaluate(position, botSide) : worstScore,
            canMateInOne);
    }

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
}
