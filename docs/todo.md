3. Is there a way to retrieve the model and effort list dynamically from the Claude Code CLI instead of hardcoding them in the code? This would make it easier to update and maintain.
  - C:\Dev\north-lab-kev\agent-control-tower\src\Act.Agents.Codex\CodexCapabilities.cs
  - C:\Dev\north-lab-kev\agent-control-tower\src\Act.Agents.ClaudeCode\ClaudeCodeCapabilities.cs


4. The application must ask confirmation before closing if there are unsaved changes. This is to prevent accidental loss of work (task view and terminal input not pushed into the CLI using "enter"). Same behavior, the application should ask to discard changes if try to go back to board while task view has pending changes.

5. The application must have a new settings: On Close, it would instead minimize to tray. The tray icon would allow exit upon confirmation only,mentionning that currently running jobs will be stopped (if any) and that scheduled task will not be executed.

6. add a settings to keep the computer awake while the application is running. This would prevent the computer from going to sleep or hibernating during long-running tasks. It must works for all supported os. Then, you can remove the "awake" text in the main page, no longer needed, it just add noise
