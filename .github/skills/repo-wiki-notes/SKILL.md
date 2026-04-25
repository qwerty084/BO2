---
name: repo-wiki-notes
description: >
  Store durable project knowledge in the repository GitHub wiki. Use when the
  user asks to save notes, document confirmed addresses, preserve research
  findings, update wiki knowledge, or record information that future agents
  should consult.
---

# Repository wiki notes

Use this skill to store important, reusable project knowledge in the BO2 GitHub wiki.

## When to use

- The user asks to store notes, findings, confirmed addresses, reverse-engineering results, workflows, or project knowledge.
- A task produces durable information that future agents should reuse, such as confirmed memory addresses or tool workflows.
- The information is not source code and does not belong in app files.

## Wiki location

- Main wiki URL: `https://github.com/qwerty084/BO2/wiki`
- Wiki git remote: `https://github.com/qwerty084/BO2.wiki.git`
- Confirmed memory addresses page: `Confirmed-Memory-Addresses.md`

## Safety rules

- Never store secrets, tokens, private credentials, personal data, or raw sensitive memory dumps.
- Keep memory-related notes read-only and Zombies/offline focused.
- Do not document anti-cheat bypassing, process hiding, code injection, or memory writing workflows.
- Do not create git commits in the main repository for wiki-only updates.

## Workflow

1. Check whether the wiki is enabled:
   ```powershell
   gh repo view qwerty084/BO2 --json hasWikiEnabled,viewerPermission,url
   ```
2. Work in a session or temp folder, not inside the main repo:
   ```powershell
   $wikiPath = "$env:USERPROFILE\.copilot\session-state\wiki\BO2.wiki"
   ```
3. Clone or update the wiki repo:
   ```powershell
   gh auth setup-git
   git clone https://github.com/qwerty084/BO2.wiki.git $wikiPath
   ```
   If the folder already exists:
   ```powershell
   git -C $wikiPath fetch origin master
   git -C $wikiPath rebase origin/master
   ```
4. Edit the relevant `.md` wiki page with `apply_patch`.
5. Commit and push only inside the wiki repository:
   ```powershell
   git -C $wikiPath add <page>.md
   git -C $wikiPath commit -m "docs: update wiki notes"
   git -C $wikiPath push
   ```

## Page style

- Use concise headings and tables.
- Include target/build context when documenting addresses or tools.
- Mark whether information is confirmed, inferred, or needs validation.
- Prefer stable page names, for example:
  - `Confirmed-Memory-Addresses.md`
  - `Memory-Reading-Workflow.md`
  - `Tooling-Notes.md`

## Address documentation template

```markdown
## Target

| Field | Value |
| --- | --- |
| Process | `t6zm.exe` |
| Mode | Zombies |
| Bitness | 32-bit |
| Access | Read-only |

## Addresses

| Stat | Address | Type | Status | Notes |
| --- | ---: | --- | --- | --- |
| Points | `0x0234C068` | `int32` | Confirmed | Works after restart. |
```
