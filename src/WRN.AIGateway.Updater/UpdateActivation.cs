using System;
using System.IO;

namespace WRN.AIGateway.Updater
{
    internal sealed class UpdateActivationResult
    {
        public bool Success { get; set; }
        public bool RolledBack { get; set; }
        public string Status { get; set; }
        public string ActiveRoot { get; set; }
        public string PreviousRoot { get; set; }
        public string FailedCandidateRoot { get; set; }
    }

    internal static class UpdateActivator
    {
        public static UpdateActivationResult Activate(
            string installRoot,
            string candidateAppRoot,
            Func<string, bool> postActivationHealthCheck)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
                throw new ArgumentNullException("installRoot");
            if (string.IsNullOrWhiteSpace(candidateAppRoot))
                throw new ArgumentNullException("candidateAppRoot");
            if (postActivationHealthCheck == null)
                throw new ArgumentNullException("postActivationHealthCheck");

            var root = Path.GetFullPath(installRoot);
            var candidate = Path.GetFullPath(candidateAppRoot);
            var stagedRoot = Path.GetFullPath(
                Path.Combine(root, "updates", "staged")
                    .TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar);

            if (!candidate.StartsWith(
                stagedRoot,
                StringComparison.OrdinalIgnoreCase))
            {
                return Result(
                    false,
                    false,
                    "UPDATE_CANDIDATE_OUTSIDE_STAGING",
                    root,
                    null);
            }

            if (!Directory.Exists(candidate))
            {
                return Result(
                    false,
                    false,
                    "UPDATE_CANDIDATE_MISSING",
                    root,
                    null);
            }

            if (Directory.Exists(
                Path.Combine(
                    root,
                    "transition",
                    "pending")))
            {
                return Result(
                    false,
                    false,
                    "UPDATE_DEFERRED_RECOVERY_PENDING",
                    root,
                    null);
            }

            var current = Path.Combine(root, "current");
            var previous = Path.Combine(root, "previous");
            var updates = Path.Combine(root, "updates");
            Directory.CreateDirectory(updates);

            using (AcquireLock(
                Path.Combine(updates, "update.lock")))
            using (AcquireTransitionLock(root))
            {
                if (!Directory.Exists(current))
                {
                    return Result(
                        false,
                        false,
                        "UPDATE_CURRENT_MISSING",
                        root,
                        null);
                }

                var retiredPrevious = Path.Combine(
                    updates,
                    "retired-previous-"
                    + Guid.NewGuid().ToString("N"));

                var failedCandidate = Path.Combine(
                    updates,
                    "failed-"
                    + DateTime.UtcNow.ToString("yyyyMMddHHmmss")
                    + "-"
                    + Guid.NewGuid().ToString("N"));

                var retiredExists = false;
                var currentMoved = false;
                var candidateActivated = false;

                try
                {
                    if (Directory.Exists(previous))
                    {
                        Directory.Move(
                            previous,
                            retiredPrevious);
                        retiredExists = true;
                    }

                    Directory.Move(
                        current,
                        previous);
                    currentMoved = true;

                    Directory.Move(
                        candidate,
                        current);
                    candidateActivated = true;

                    if (!postActivationHealthCheck(current))
                    {
                        throw new InvalidDataException(
                            "UPDATE_POST_ACTIVATION_HEALTH_FAILED");
                    }

                    if (retiredExists
                        && Directory.Exists(retiredPrevious))
                    {
                        Directory.Delete(
                            retiredPrevious,
                            true);
                    }

                    return Result(
                        true,
                        false,
                        "UPDATE_ACTIVATED",
                        root,
                        null);
                }
                catch
                {
                    var rollbackOk = true;

                    try
                    {
                        if (candidateActivated
                            && Directory.Exists(current))
                        {
                            Directory.Move(
                                current,
                                failedCandidate);
                        }
                    }
                    catch
                    {
                        rollbackOk = false;
                    }

                    try
                    {
                        if (currentMoved
                            && Directory.Exists(previous)
                            && !Directory.Exists(current))
                        {
                            Directory.Move(
                                previous,
                                current);
                        }
                    }
                    catch
                    {
                        rollbackOk = false;
                    }

                    try
                    {
                        if (retiredExists
                            && Directory.Exists(retiredPrevious)
                            && !Directory.Exists(previous))
                        {
                            Directory.Move(
                                retiredPrevious,
                                previous);
                        }
                    }
                    catch
                    {
                        rollbackOk = false;
                    }

                    return new UpdateActivationResult
                    {
                        Success = false,
                        RolledBack = rollbackOk,
                        Status = rollbackOk
                            ? "UPDATE_ACTIVATION_ROLLED_BACK"
                            : "UPDATE_ROLLBACK_INCOMPLETE",
                        ActiveRoot = current,
                        PreviousRoot = previous,
                        FailedCandidateRoot =
                            Directory.Exists(failedCandidate)
                                ? failedCandidate
                                : null
                    };
                }
            }
        }

        private static FileStream AcquireTransitionLock(
            string installRoot)
        {
            var transition = Path.Combine(
                installRoot,
                "transition");
            Directory.CreateDirectory(transition);

            return AcquireLock(
                Path.Combine(
                    transition,
                    "transition.lock"));
        }

        private static FileStream AcquireLock(
            string path)
        {
            var parent =
                Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            return new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }

        private static UpdateActivationResult Result(
            bool success,
            bool rolledBack,
            string status,
            string installRoot,
            string failedCandidate)
        {
            return new UpdateActivationResult
            {
                Success = success,
                RolledBack = rolledBack,
                Status = status,
                ActiveRoot =
                    Path.Combine(
                        installRoot,
                        "current"),
                PreviousRoot =
                    Path.Combine(
                        installRoot,
                        "previous"),
                FailedCandidateRoot =
                    failedCandidate
            };
        }
    }
}
