namespace Maple.Host.Stationary;

public sealed class SelfIdentityStabilizer(
    double minimumAcquisitionScore = 0.90,
    double minimumTrackingScore = 0.86,
    double minimumPeakMargin = 0.06,
    int requiredFrames = 3,
    double maximumJumpPx = 12,
    double minimumTrackingPeakMargin = 0,
    bool preferHighestLocalScore = false)
{
    private long lastSequence;
    private double? lastAcceptedX;
    private double? lastAcceptedY;
    private double? lastTrustedX;
    private double? lastTrustedY;
    private int streak;
    private bool wasTrusted;

    public void Reset()
    {
        if (!wasTrusted)
        {
            lastAcceptedX = null;
            lastAcceptedY = null;
            lastTrustedX = null;
            lastTrustedY = null;
        }
        streak = 0;
    }

    public SelfIdentityObservation Update(SelfNameMatch match, bool allowTrackingAnchorAdvance = true)
    {
        bool newer = match.FrameSequence > lastSequence;
        if (newer) lastSequence = match.FrameSequence;
        Candidate? candidate = newer ? SelectCandidate(match) : null;
        if (!candidate.HasValue)
        {
            string failureCode = FailureCode(match, newer);
            streak = 0;
            if (!wasTrusted)
            {
                lastAcceptedX = null;
                lastAcceptedY = null;
            }
            return new SelfIdentityObservation(
                wasTrusted ? SelfIdentityStatus.Untrusted : SelfIdentityStatus.Acquiring,
                match.FrameSequence,
                null,
                null,
                match.BestScore,
                failureCode);
        }

        lastAcceptedX = candidate.Value.X;
        lastAcceptedY = candidate.Value.Y;
        streak++;
        if (streak < requiredFrames)
            return new SelfIdentityObservation(
                SelfIdentityStatus.Acquiring,
                match.FrameSequence,
                candidate.Value.X,
                candidate.Value.Y,
                candidate.Value.Score,
                "VISUAL_SELF_ACQUIRING");

        bool firstTrustedFrame = !wasTrusted;
        wasTrusted = true;
        if (firstTrustedFrame || allowTrackingAnchorAdvance)
        {
            lastTrustedX = candidate.Value.X;
            lastTrustedY = candidate.Value.Y;
        }
        return new SelfIdentityObservation(
            SelfIdentityStatus.Trusted,
            match.FrameSequence,
            candidate.Value.X,
            candidate.Value.Y,
            candidate.Value.Score,
            "VISUAL_SELF_TRUSTED");
    }

    private Candidate? SelectCandidate(SelfNameMatch match)
    {
        if (!match.HasCandidate) return null;
        if (!wasTrusted)
        {
            if (match.BestScore < minimumAcquisitionScore ||
                match.BestScore - match.SecondBestScore < minimumPeakMargin)
                return null;
            var candidate = new Candidate(match.BestScore, match.CenterX, match.CenterY);
            return !lastAcceptedX.HasValue || !lastAcceptedY.HasValue || IsWithinJump(candidate)
                ? candidate
                : null;
        }

        Candidate[] local =
        [
            new(match.BestScore, match.CenterX, match.CenterY),
            new(match.SecondBestScore, match.SecondCenterX, match.SecondCenterY)
        ];
        if (preferHighestLocalScore)
        {
            if (!IsLocal(local[0].Score, local[0].X, local[0].Y)) return null;
            if (IsLocal(local[1].Score, local[1].X, local[1].Y) &&
                local[0].Score - local[1].Score < minimumTrackingPeakMargin)
                return null;
            return local[0];
        }
        Candidate[] acceptable = local
            .Where(candidate => IsLocal(candidate.Score, candidate.X, candidate.Y))
            .OrderBy(DistanceFromLastAccepted)
            .ToArray();
        if (acceptable.Length == 0) return null;
        if (acceptable.Length == 1) return acceptable[0];
        if (Math.Abs(acceptable[0].Score - acceptable[1].Score) < minimumTrackingPeakMargin)
            return null;
        return Math.Abs(
            DistanceFromLastAccepted(acceptable[0]) -
            DistanceFromLastAccepted(acceptable[1])) < 1
            ? null
            : acceptable[0];
    }

    private string FailureCode(SelfNameMatch match, bool newer)
    {
        if (!newer) return "VISUAL_FRAME_NOT_NEW";
        if (!match.HasCandidate) return match.Code;
        if (!wasTrusted)
        {
            if (match.BestScore < minimumAcquisitionScore) return "VISUAL_NAME_SCORE_LOW";
            if (match.BestScore - match.SecondBestScore < minimumPeakMargin)
                return "VISUAL_NAME_AMBIGUOUS";
            return lastAcceptedX.HasValue && lastAcceptedY.HasValue &&
                !IsWithinJump(new Candidate(match.BestScore, match.CenterX, match.CenterY))
                ? "VISUAL_SELF_JUMP"
                : "VISUAL_NAME_SCORE_LOW";
        }
        if (match.BestScore < minimumTrackingScore && match.SecondBestScore < minimumTrackingScore)
            return "VISUAL_NAME_SCORE_LOW";
        Candidate[] local =
        [
            new(match.BestScore, match.CenterX, match.CenterY),
            new(match.SecondBestScore, match.SecondCenterX, match.SecondCenterY)
        ];
        if (preferHighestLocalScore)
        {
            if (!IsLocal(local[0].Score, local[0].X, local[0].Y)) return "VISUAL_SELF_JUMP";
            return IsLocal(local[1].Score, local[1].X, local[1].Y) &&
                local[0].Score - local[1].Score < minimumTrackingPeakMargin
                ? "VISUAL_NAME_AMBIGUOUS"
                : "VISUAL_SELF_JUMP";
        }
        double[] distances = local
            .Where(candidate => IsLocal(candidate.Score, candidate.X, candidate.Y))
            .Select(DistanceFromLastAccepted)
            .Order()
            .ToArray();
        if (distances.Length > 1 && Math.Abs(distances[0] - distances[1]) < 1)
            return "VISUAL_NAME_AMBIGUOUS";
        Candidate[] acceptable = local.Where(candidate =>
            IsLocal(candidate.Score, candidate.X, candidate.Y)).ToArray();
        if (acceptable.Length > 1 &&
            Math.Abs(acceptable[0].Score - acceptable[1].Score) < minimumTrackingPeakMargin)
            return "VISUAL_NAME_AMBIGUOUS";
        return "VISUAL_SELF_JUMP";
    }

    private double DistanceFromLastAccepted(Candidate candidate)
    {
        (double referenceX, double referenceY) = TrackingReference();
        double deltaX = candidate.X - referenceX;
        double deltaY = candidate.Y - referenceY;
        return deltaX * deltaX + deltaY * deltaY;
    }

    private bool IsWithinJump(Candidate candidate) =>
        lastAcceptedX.HasValue &&
        lastAcceptedY.HasValue &&
        Math.Abs(candidate.X - lastAcceptedX.Value) <= maximumJumpPx &&
        Math.Abs(candidate.Y - lastAcceptedY.Value) <= maximumJumpPx;

    private bool IsLocal(double score, double x, double y) =>
        score >= minimumTrackingScore &&
        !double.IsNaN(x) &&
        !double.IsNaN(y) &&
        IsInsideTrackingAnchor(x, y);

    private bool IsInsideTrackingAnchor(double x, double y)
    {
        if (!lastAcceptedX.HasValue || !lastAcceptedY.HasValue) return false;
        (double referenceX, double referenceY) = TrackingReference();
        return Math.Abs(x - referenceX) <= maximumJumpPx &&
            Math.Abs(y - referenceY) <= maximumJumpPx;
    }

    private (double X, double Y) TrackingReference() =>
        preferHighestLocalScore && wasTrusted && lastTrustedX.HasValue && lastTrustedY.HasValue
            ? (lastTrustedX.Value, lastTrustedY.Value)
            : (lastAcceptedX!.Value, lastAcceptedY!.Value);

    private readonly record struct Candidate(double Score, double X, double Y);
}
