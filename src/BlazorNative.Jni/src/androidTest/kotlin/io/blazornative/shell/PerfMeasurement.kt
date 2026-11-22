package io.blazornative.shell

/**
 * Marks a test class as a phase 19.2 measurement, not a correctness test. The PR lane and the
 * nightly run exclude it through `notAnnotation` in build.gradle.kts, so the instrumented
 * baseline stays at its asserted count. `-PperfOnly` flips the filter to `annotation=` and runs
 * only these classes. Runtime retention is required: the runner reads it by reflection.
 */
@Retention(AnnotationRetention.RUNTIME)
@Target(AnnotationTarget.CLASS)
annotation class PerfMeasurement
