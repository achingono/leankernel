# Match Chat Composer Layout to Target Design

This plan outlines the changes required to align the chat composer and tool button layout in `Chat.razor` with the target design shown in the screenshot.

## Proposed Changes

### Chat Page & Composer Component

Modify the chat composer layout to merge the input field and actions toolbar into a single card container with a shared border. Update the action buttons to use the correct icons (formatting, emoji, attachment, loop component, add) and position the send button on the right side of the toolbar with a vertical divider. Add the "Shift+Enter starts a new line" hint text at the bottom right.

#### [MODIFY] [Chat.razor](../../src/LeanKernel.Gateway/Components/Pages/Chat.razor)

- Simplify `FluentTextArea` settings by reducing `Rows` to `1` so it starts compact.
- Group formatting, emoji, file attachment, loop component, and plus action buttons inside `.teams-composer-tools` with the updated regular size 16 icons:
  - Formatting: `new Icons.Regular.Size16.TextEditStyle()`
  - Emoji: `new Icons.Regular.Size16.Emoji()`
  - Attachment: `new Icons.Regular.Size16.Attach()`
  - Loop Component: `new Icons.Regular.Size16.ArrowRepeatAll()`
  - Add: `new Icons.Regular.Size16.Add()`
- Add a vertical separator element between the tools and the send button.
- Change the send button to use lightweight appearance (`Appearance="Appearance.Lightweight"`).
- Rearrange elements so that the toolbar is aligned to the right inside the composer surface.
- Add a hint container below the composer surface for "Shift+Enter starts a new line."

#### [MODIFY] [app.css](../../src/LeanKernel.Gateway/wwwroot/css/app.css)

- Update `.teams-composer-surface` to use flexbox (flex column layout) instead of relative positioning, which avoids overlapping issues between textarea and footer.
- Disable borders, background color, and box shadow on the internal `<fluent-text-area>` and its parts to make only the outer card's border visible.
- Reduce `.teams-composer-input` min-height to `2.5rem` for a more compact single-line default height.
- Update `.teams-composer-footer` to remove absolute positioning and flow naturally at the bottom of the flex container, aligning all actions (tools + send button) to the right.
- Add styles for the vertical separator `.teams-composer-separator`.
- Style `.teams-send-button` as a lightweight circular button that highlights with the primary accent color when active (not disabled).
- Style `.teams-composer-hint` to place the helper text at the bottom-right of the composer shell.

## Verification Plan

### Automated Tests
- Build and verify the application compiles without errors:
  ```bash
  dotnet build src/LeanKernel.sln
  ```

### Manual Verification
- Deploy/run the application and verify the UI changes:
  1. Inspect the chat composer layout at `http://localhost:5080/chat`.
  2. Confirm the outer border wraps both the text input and bottom-right toolbar actions.
  3. Verify the layout and alignment of the tool buttons (formatting, emoji, attach, loop, plus) and the send button with a vertical separator in between.
  4. Ensure the hint text "Shift+Enter starts a new line." is aligned properly at the bottom right.
  5. Test that typing text enables the send button and styles the send icon blue, and clearing the text disables the button (greying it out).
