## 2024-03-24 - Added tooltips to icon-only buttons in WPF
**Learning:** WPF icon-only buttons need `ToolTip` and `AutomationProperties.Name` attributes, which act like ARIA labels for screen readers.
**Action:** Always verify icon-only UI elements have both a visual ToolTip and an AutomationProperties.Name to ensure full accessibility in WPF.
