# UI Layout Verification and Recursive Self-Correction Rule

## 1. Overview and Core Principles
- When adding or modifying UI components, or generating theme preview images (screenshots), the agent must **detect text interference between UI elements on its own (truncated text, overlapping or colliding controls, insufficient margins, awkward line breaks) and fix it recursively**.
- Before the user points out a layout defect, the agent must visually inspect (`view_file`) the screenshot assets generated automatically after the build, check for problems, and complete a self-improvement loop of fixing the code and rebuilding until no defects remain.

## 2. Mandatory Verification Checklist
1. **Full multilingual (Korean / English) verification**:
   - Korean and English differ in character width and word length, so when switching languages (KOR ↔ ENG), check in both languages that no text in labels, buttons, checkboxes or drop-down combo boxes is truncated or intrudes on other controls.
   - Give drop-downs (ComboBox) enough width that their text (e.g. "Bottom-Right (Default)") is not elided or truncated when selected.
2. **Full dark / light theme verification**:
   - In both dark mode and light mode, check that the controls' background colour, foreground text colour and border contrast do not impair legibility.
3. **Safe margins between controls**:
   - Keep a safe margin of at least 16px between adjacent controls and around buttons. If space is short, enlarge the form or modularise the layout structure and rearrange it.
4. **Recursive self-correction loop**:
   - If any minor interference or overlap is found while reviewing screenshots, fix the code immediately, rebuild and regenerate the screenshots, and report completion only after visually confirming the result is flawless.

## 3. Multilingual Matching of GitHub Preview Images
- **Match images to each language section**:
  - In the English section of public documents such as `README.md`, use the English screenshots (`screenshot_dark.png`, `screenshot_light.png`).
  - In the Korean section, use the Korean screenshots (`screenshot_ko_dark.png`, `screenshot_ko_light.png`), giving both domestic and international users the best visual readability.
  - Keep the default thumbnail (`screenshot.png`) as English dark mode for global compatibility.
