import Testing

// SwiftPM is not used (see README), so the test binary provides its own entry point.
@main
struct TestMain {
    static func main() async {
        await Testing.__swiftPMEntryPoint() as Never
    }
}
