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
    private bool relocationPending;

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
        relocationPending = false;
    }

    public SelfIdentityObservation Update(
        SelfNameMatch match,
        bool allowTrackingAnchorAdvance = true,
        bool allowRelocation = false)
    {
        bool newer = match.FrameSequence > lastSequence;
        if (newer) lastSequence = match.FrameSequence;
        CandidateSelection? selection = newer ? SelectCandidate(match, allowRelocation) : null;
        if (!selection.HasValue)
        {
            string failureCode = FailureCode(match, newer, allowRelocation);
            streak = 0;
            relocationPending = false;
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

        Candidate candidate = selection.Value.Candidate;
        if (selection.Value.IsRelocation)
        {
            bool continuesRelocation = relocationPending && IsWithinJump(candidate);
            if (!continuesRelocation) streak = 0;
            relocationPending = true;
        }
        else if (relocationPending)
        {
            streak = 0;
            relocationPending = false;
        }

        lastAcceptedX = candidate.X;
        lastAcceptedY = candidate.Y;
        streak++;
        if (streak < requiredFrames)
            return new SelfIdentityObservation(
                SelfIdentityStatus.Acquiring,
                match.FrameSequence,
                candidate.X,
                candidate.Y,
                candidate.Score,
                "VISUAL_SELF_ACQUIRING");

        bool firstTrustedFrame = !wasTrusted;
        wasTrusted = true;
        if (firstTrustedFrame || allowTrackingAnchorAdvance || selection.Value.IsRelocation)
        {
            lastTrustedX = candidate.X;
            lastTrustedY = candidate.Y;
        }
        relocationPending = false;
        return new SelfIdentityObservation(
            SelfIdentityStatus.Trusted,
            match.FrameSequence,
            candidate.X,
            candidate.Y,
            candidate.Score,
            "VISUAL_SELF_TRUSTED");
    }

    private CandidateSelection? SelectCandidate(SelfNameMatch match, bool allowRelocation)
    {
        if (!match.HasCandidate) return null;
        if (!wasTrusted)
        {
            if (match.BestScore < minimumAcquisitionScore ||
                match.BestScore - match.SecondBestScore < minimumPeakMargin)
                return null;
            var candidate = new Candidate(match.BestScore, match.CenterX, match.CenterY);
            return !lastAcceptedX.HasValue || !lastAcceptedY.HasValue || IsWithinJump(candidate)
                ? new CandidateSelection(candidate, false)
                : null;
        }

        Candidate[] local =
        [
            new(match.BestScore, match.CenterX, match.CenterY),
            new(match.SecondBestScore, match.SecondCenterX, match.SecondCenterY)
        ];
        if (preferHighestLocalScore)
        {
            if (IsLocal(local[0].Score, local[0].X, local[0].Y))
            {
                if (IsLocal(local[1].Score, local[1].X, local[1].Y) &&
                    local[0].Score - local[1].Score < minimumTrackingPeakMargin)
                    return null;
                return new CandidateSelection(local[0], false);
            }
            return allowRelocation && IsUniqueAcquisitionCandidate(match)
                ? new CandidateSelection(local[0], true)
                : null;
        }
        Candidate[] acceptable = local
            .Where(candidate => IsLocal(candidate.Score, candidate.X, candidate.Y))
            .OrderBy(DistanceFromLastAccepted)
            .ToArray();
        if (acceptable.Length == 0) return null;
        if (acceptable.Length == 1) return new CandidateSelection(acceptable[0], false);
        if (Math.Abs(acceptable[0].Score - acceptable[1].Score) < minimumTrackingPeakMargin)
            return null;
        Candidate? selected = Math.Abs(
            DistanceFromLastAccepted(acceptable[0]) -
            DistanceFromLastAccepted(acceptable[1])) < 1
            ? null
            : acceptable[0];
        return selected.HasValue ? new CandidateSelection(selected.Value, false) : null;
    }

    private bool IsUniqueAcquisitionCandidate(SelfNameMatch match) =>
        match.BestScore >= minimumAcquisitionScore &&
        match.BestScore - match.SecondBestScore >= minimumPeakMargin &&
        !double.IsNaN(match.CenterX) &&
        !double.IsNaN(match.CenterY);

    private string FailureCode(SelfNameMatch match, bool newer, bool allowRelocation)
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
            if (allowRelocation && !IsLocal(local[0].Score, local[0].X, local[0].Y))
            {
                if (match.BestScore < minimumAcquisitionScore) return "VISUAL_NAME_SCORE_LOW";
                if (match.BestScore - match.SecondBestScore < minimumPeakMargin)
                    return "VISUAL_NAME_AMBIGUOUS";
            }
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
    private readonly record struct CandidateSelection(Candidate Candidate, bool IsRelocation);
}
