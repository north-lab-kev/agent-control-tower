1. separate electron concerns in an inteface:
- SessionView.razor.cs
- TaskView.razor.cs
- why not use in program.cs -> HybridSupport.IsElectronActive ? will it works in the setup installed?


2. implement more unit tests, should some classes have interfaces to allow more unitestability ?
3. full code review and refactor to improve maintainability and readability
4. replace agent preamble using file to call endpoint instead on the localhost, same as hooks, would it trigger a permission prompt?
