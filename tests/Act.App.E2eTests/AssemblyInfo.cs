// One server and one browser at a time. Two reasons, and either alone would be enough: `ActApp`
// configures the app through process environment variables, which two concurrent hosts would clobber;
// and a browser suite that runs itself in parallel on one machine is how a green suite becomes a
// flaky one.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
