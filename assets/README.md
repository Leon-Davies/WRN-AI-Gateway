# Brand assets

Production WRN brand imagery is intentionally not stored in this public repository.

The application supports an optional local development asset at:

`src/WRN.AIGateway/local-assets/wrn-hero.png`

That directory is ignored by git. During local builds, `scripts/build.ps1` copies the image into the disposable `dist/assets` folder.

If no local brand image is present, WRN AI Gateway uses a built-in gradient fallback.

Before official brand imagery is committed or distributed, confirm that the repository/distribution location is appropriate for those corporate assets.
