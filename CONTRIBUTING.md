# Contributing

Contributions are welcome. Please improve the original rather than starting a
separate copy: your work then reaches every player who uses the app, and your
name is shown in the app itself, on its About page (from CONTRIBUTORS.md).

## Ways to help

- Report a bug: what you did, what happened, and the log from the app's
  `Data\logs` folder.
- Add or correct a game profile in `src/UEManager.Core/Games/Profiles`.
- Fix a bug or add a feature. For a larger change, open an issue first, so that
  we agree on the approach before you spend time on it.

## Pull requests

1. Fork the repository and create a branch.
2. Build and test. This needs the .NET 10 SDK:

   ```powershell
   dotnet build src/UEManager.slnx
   dotnet test src/UEManager.slnx
   ```

3. Write code like the code around it, and add tests for changes in
   `UEManager.Core`.
4. Open the pull request, and say what it changes and why.

## Credit

With your first merged contribution, add yourself to CONTRIBUTORS.md in the same
pull request, or ask and the maintainer adds you: your name or GitHub name, and
what you did. A bug report that led to a fix counts too.

## License of contributions

The program is licensed under the GNU General Public License, version 3, with the
additional terms in NOTICE.md. By sending a contribution, you agree that it is
licensed under the same terms, and you confirm that you have the right to
contribute it.
