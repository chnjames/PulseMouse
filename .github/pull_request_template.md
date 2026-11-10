## Summary

Explain the user-visible change and why it is needed.

## Verification

- [ ] `dotnet build PulseMouse.slnx -c Release --no-restore`
- [ ] `dotnet run --project tests/PulseMouse.Core.SmokeTests -c Release --no-build`
- [ ] Relevant manual checks in `docs/RELEASE_CHECKLIST.md`

## Checklist

- [ ] Visible text is localized in Chinese and English.
- [ ] No build output, personal settings, or logs are included.
- [ ] UI changes include screenshots when useful.
