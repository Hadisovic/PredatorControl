# Security policy

## Versions and platforms

Security work in this repository currently targets the source on `revisions`. This audit does not establish an owner-approved maintenance window for historical releases.

| Version/source | Security status |
|---|---|
| Current `revisions` source | Contains the remediations documented in `SECURITY_FIXES.md`; Windows/physical-device validation limitations apply |
| Published v1.4.2 and tracked binaries | Separate distribution artifacts; source fixes do not update them automatically |
| Older versions | Not covered by this audit; migrate to an owner-designated fixed release when available |

The application targets Windows 10/11 x64 and .NET 10 Desktop Runtime, with source currently requiring runtime patch 10.0.14. Hardware controls depend on the compiled device profile and trusted, operational OEM interfaces. A successful build does not establish compatibility with every laptop/BIOS configuration.

## Reporting a vulnerability privately

Use [GitHub private vulnerability reporting](https://github.com/Hadisovic/PredatorControl/security/advisories/new) when enabled. The repository owner must enable it in the repository's security settings if unavailable. If no private reporting route is enabled, request a private contact method without posting exploit details, sensitive logs or credentials in a public issue. No security email address is designated here.

Include the affected source commit/release, Windows/runtime version, attack prerequisites, expected versus observed behavior, and a minimal safe reproduction using temporary files or mocked hardware. Redact usernames, serial numbers, tokens and unnecessary local paths. Do not test unsafe fan/thermal behavior or target another user's device.

Coordinate disclosure and a remediation date privately with the maintainer. Public reports should follow availability of a fix and practical update guidance. Reported secrets require owner-led revocation/rotation; removing a string from the latest source alone does not revoke it.

## Updating and release requirements

Use an owner-designated fixed release. Portable executables in user-writable folders cannot be promoted to privileged startup tasks or used for automatic replacement. Start with Windows requires a copy in an administrator-protected location.

Automatic installation requires trusted Authenticode signatures on both installed and new executables, the same signing public key, matching newer executable/release versions, HTTPS and SHA-256 integrity checks. Unsigned builds retain update discovery but require manual installation. A checksum hosted with an executable is not independent publisher authentication. Establish the first trusted signed installation and any signing-key change through a separately verified manual process.

Owners must publish rebuilt, tested artifacts before these source fixes reach existing users; establish signing/timestamping and secure key custody; designate supported releases; enable private reporting; and configure branch protections/required checks. Do not distribute private keys or credentials in the repository or workflow files. See `SECURITY_AUDIT.md` and `VERIFICATION_RESULTS.md` before shipping.

The audited Actions workflow is prepared locally but is not published, by owner choice after GitHub reported missing OAuth `workflow` scope. The test runner can be used locally until the owner authorizes workflow publication.
