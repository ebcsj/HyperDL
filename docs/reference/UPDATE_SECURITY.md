# Update security status

Automatic updating is not implemented. HyperDL has no update feed, updater
package, signing key, Ed25519 verifier, installer execution path, or downgrade
policy.

Do not distribute unsigned update packages or run downloaded installers from
the ordinary download flow. A future updater requires a fixed HTTPS origin,
developer-controlled signing infrastructure, signature and hash validation,
version policy, and explicit user confirmation.
