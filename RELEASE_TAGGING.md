# Release Tagging Quick Steps

Use this flow to avoid extra manual steps and keep tags aligned with the exact commit you want to release.

## One-time flow per release

1. Stage and commit all changes (including version bump).
2. Push your branch.
3. Create a tag on that same commit.
4. Push the tag.

## Commands

```bash
git add -A
git commit -m "Release prep: v1.0.1-draft"
# Push the latest code, is NOT yet a release and will NOT be downloaded by install script
git push
git tag v1.0.1-draft
# Push the tagged version and trigger the release workflow
git push origin v1.0.1-draft
```

## Notes

- You do **not** tag uncommitted changes.
- This still triggers two GitHub Actions runs:
  - branch CI run (from `git push`)
  - tag/release run (from `git push origin <tag>`)
- That is expected and normal.
