using GitVersion.Common;
using GitVersion.Extensions;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GitVersion.VersionCalculation
{
    /// <summary>
    /// Version is extracted from all tags on the branch which are valid, and not newer than the current commit.
    /// BaseVersionSource is the tag's commit.
    /// Increments if the tag is not the current commit.
    /// </summary>
    public class TaggedCommitVersionStrategy : VersionStrategyBase
    {
        private readonly IRepositoryMetadataProvider repositoryMetadataProvider;

        public TaggedCommitVersionStrategy(IRepositoryMetadataProvider repositoryMetadataProvider, Lazy<GitVersionContext> versionContext) : base(versionContext)
        {
            this.repositoryMetadataProvider = repositoryMetadataProvider ?? throw new ArgumentNullException(nameof(repositoryMetadataProvider));
        }

        public override IEnumerable<BaseVersion> GetVersions()
        {
            return GetTaggedVersions(Context.CurrentBranch, Context.CurrentCommit.When());
        }

        internal IEnumerable<BaseVersion> GetTaggedVersions(Branch currentBranch, DateTimeOffset? olderThan)
        {
            var servicePrefix = Context.FullConfiguration.ServicePrefix;

            var allTags = repositoryMetadataProvider.GetValidVersionTags(Context.Configuration.GitTagPrefix, olderThan, servicePrefix);

            var majorVersionTags = new List<Tuple<Tag, SemanticVersion>>();
            foreach (var tag in allTags)
            {
                if (tag.Item1.FriendlyName.Split('-').Length == 1)
                {
                    majorVersionTags.Add(tag);
                }
            }

            var patchVersionTags = new List<Tuple<Tag, SemanticVersion>>();
            if (!string.IsNullOrEmpty(Context.Configuration.Tag))
            {
                foreach (var tag in allTags)
                {
                    if (tag.Item1.FriendlyName.Split('-').Length > 1
                        && tag.Item1.FriendlyName.Contains(Context.Configuration.Tag))
                    {
                        patchVersionTags.Add(tag);
                    }
                }
            }

            var releasedTagsOnBranch = currentBranch
                .Commits
                .SelectMany(commit => { return majorVersionTags; })
                .Select(t =>
                {
                    if (t.Item1.PeeledTarget() is Commit)
                        return new VersionTaggedCommit(t.Item1.PeeledTarget() as Commit, t.Item2, t.Item1.FriendlyName.Replace($"{servicePrefix}/", string.Empty));

                    return null;
                })
                .Where(a => a != null)
                .Take(5)
                .ToList();

            var patchTagsOnBranch = currentBranch
                .Commits
                .SelectMany(commit => { return patchVersionTags; })
                .Select(t =>
                {
                    if (t.Item1.PeeledTarget() is Commit)
                        return new VersionTaggedCommit(t.Item1.PeeledTarget() as Commit, t.Item2, t.Item1.FriendlyName.Replace($"{servicePrefix}/", string.Empty));

                    return null;
                })
                .Where(a => a != null)
                .Take(5)
                .ToList();

            var result = new List<BaseVersion>();
            releasedTagsOnBranch.ForEach(t =>
            {
                if (!result.Any(b => b.SemanticVersion == t.SemVer))
                {
                    result.Add(CreateBaseVersion(Context, t, true));
                }
            });
            patchTagsOnBranch.ForEach(t =>
            {
                if (!result.Any(b => b.SemanticVersion == t.SemVer))
                {
                    result.Add(CreateBaseVersion(Context, t, true));
                }
            });

            return result
                .OrderByDescending(t => t.BaseVersionSource.Author.When)
                .ToList();
        }

        private BaseVersion CreateBaseVersion(GitVersionContext context, VersionTaggedCommit version, bool? updateVersion = null)
        {
            var shouldUpdateVersion = !updateVersion.HasValue
                ? version.Commit.Sha != context.CurrentCommit.Sha
                : updateVersion.Value;
            var baseVersion = new BaseVersion(FormatSource(version), shouldUpdateVersion, version.SemVer, version.Commit, null);
            return baseVersion;
        }

        protected virtual string FormatSource(VersionTaggedCommit version)
        {
            return $"Git tag '{version.Tag}'";
        }

        protected virtual bool IsValidTag(Tag tag, Commit commit)
        {
            return tag.PeeledTarget() == commit;
        }

        protected class VersionTaggedCommit
        {
            public string Tag;
            public Commit Commit;
            public SemanticVersion SemVer;

            public VersionTaggedCommit(Commit commit, SemanticVersion semVer, string tag)
            {
                Tag = tag;
                Commit = commit;
                SemVer = semVer;
            }

            public override string ToString()
            {
                return $"{Tag} | {Commit} | {SemVer}";
            }
        }
    }
}
