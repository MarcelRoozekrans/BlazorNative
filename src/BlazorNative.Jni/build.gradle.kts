import org.gradle.api.tasks.testing.logging.TestExceptionFormat

plugins {
    kotlin("jvm") version "2.0.21"
}

group = "io.blazornative"
version = "0.1.0-SNAPSHOT"

repositories {
    mavenCentral()
}

dependencies {
    // JNA — JVM ↔ libwasmtime FFI binding
    implementation("net.java.dev.jna:jna:5.14.0")
    implementation("net.java.dev.jna:jna-platform:5.14.0")

    // Kotlin stdlib
    implementation(kotlin("stdlib-jdk8"))

    // Tests
    testImplementation("org.junit.jupiter:junit-jupiter-api:5.11.3")
    testImplementation("org.junit.jupiter:junit-jupiter-params:5.11.3")
    testRuntimeOnly("org.junit.jupiter:junit-jupiter-engine:5.11.3")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher:1.11.3")
}

kotlin {
    // JDK 21 (Temurin LTS) is installed by setup.ps1 section 6. Gradle 8.11.1
    // daemon supports JDK 8-23; using 21 keeps the project + daemon JVM aligned.
    jvmToolchain(21)
}

tasks.test {
    useJUnitPlatform()
    testLogging {
        events("passed", "skipped", "failed")
        exceptionFormat = TestExceptionFormat.FULL
        showStandardStreams = true
    }
    // Path to the AOT'd .wasm — emitted by `dotnet publish -r wasi-wasm -c Release`
    systemProperty(
        "wasm.path",
        rootProject.projectDir
            .resolve("../../src/BlazorNative.WasiHost/bin/Release/net10.0/wasi-wasm/AppBundle/BlazorNative.WasiHost.wasm")
            .absolutePath
    )
    // JNA's library search path — where setup.ps1 copies wasmtime.dll
    systemProperty(
        "jna.library.path",
        rootProject.projectDir
            .resolve("../../vendor/wasmtime")
            .absolutePath
    )
}
