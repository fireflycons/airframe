# Project Rules & Guidelines

## 1. UI Layout Verification & Recursive Self-Correction
- **Autonomous visual verification**: When adding or changing UI components, or generating theme preview images (screenshots), the agent must inspect the generated images itself (`view_file`), **judge on its own whether any UI text collides, and fix it recursively**: clipped text, overlapping or colliding controls, insufficient spacing and so on.
- **Multi-theme verification**:
  - In both dark and light mode, check that each control's background colour, foreground text colour and border contrast don't harm legibility.
- **Safe margins and expanding space**:
  - Leave generous safe margins between adjacent controls and around buttons. Where space is tight, enlarge the form's width or height as needed to improve the visual finish.
- **Self-improvement loop**:
  - The agent should find problems itself before the user points them out, and complete the loop of code change -> rebuild -> refresh and recheck screenshots before giving its final report.

## 2. General Project Rules
- **Readability first**: Use intuitive variable names and keep logic simple.
- **Comments required**: Explain why the code was written this way, rather than what it does.
- **Modularity**: Split code into files by feature so no single file grows too long.
- **Error handling**: Always consider exceptional cases when writing code.
- **Conclusion-first answers**: Write in the order [conclusion] -> [details].

## 3. GitHub Preview Assets Rule
- Keep the default thumbnail (`assets/screenshot.png`) as the dark-mode image.

## 4. README Documentation Rule (feature-focused and extremely concise)
- **Exclude simple layout/style changes**: Don't record simple UI or internal changes in the README, such as moving buttons, adjusting panel spacing, changing label text or build scripts.
- **Feature-focused**: List only the core features that give users real value.
- **As concise as possible**: Leave out lengthy explanations. Write only concise one-line bullet points that make the key points clear at a glance.
