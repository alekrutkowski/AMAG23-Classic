# Security and privacy

This app edits real keyboard and macro configuration. Treat backup files as sensitive: macros can contain passwords, access tokens, personal text, or internal shortcuts.

- Never publish unredacted backup JSON or HEX files in an issue or pull request.
- The app only serves its GUI on `127.0.0.1` and uses a random session token to protect its local API. Do not forward the local server port or share its token.
- USB configuration uploads can be interrupted and are not atomic. Keep the keypad connected and use a verified backup.
- The app respects OS/organization execution and USB access restrictions; it does not attempt to bypass them.

To report a security issue privately, contact **alek.rutkowski@gmail.com** with a concise description and steps to reproduce. Please avoid attaching actual secret-containing macros.
