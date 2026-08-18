# Updating TWIN A Control Center

Starting with TWIN A 0.9.1, the installed Windows launcher can check for new published releases automatically.

## For normal users

TWIN A checks the official GitHub Releases feed shortly after startup and then periodically while it is running. You can also right-click the TWIN A tray icon and choose **Check for Updates** at any time.

When a newer version is available, TWIN A shows the installed and available versions and asks before changing the installation. If you choose to update, TWIN A downloads only the expected Windows installer from this repository's official GitHub Release, verifies the release asset size and SHA-256 digest, asks Windows for administrator permission, installs the update, and starts TWIN A again.

If automatic updating cannot be completed safely, TWIN A does not install the downloaded file and can open the official Releases page instead.

## Release rule

Installed applications follow **published GitHub Releases**, not arbitrary commits on `main`. A new version should therefore be assigned in `frontend/package.json`, built and tested by CI, and published as a verified release before installed users receive it.

Example version progression:

```text
0.9.0 -> 0.9.1 -> 0.9.2 -> 0.10.0 -> 1.0.0
```

## Upgrade from 0.9.0

Version 0.9.0 was released before the self-updater existed. Existing 0.9.0 installations need one manual upgrade to 0.9.1 from the official GitHub Releases page. After 0.9.1 is installed, future published versions can be discovered by the built-in updater.
