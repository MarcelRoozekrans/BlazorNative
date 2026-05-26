# BlazorNative — Developer Makefile
# ──────────────────────────────────────────────────────────────────────────────
# make dev          → start dev host with hot reload (normal .NET, no WASM)
# make wasi         → publish WasiHost to WASM/WASI (Mono-AOT via wasi-sdk)
# make wasi-run     → publish + run via wasmtime
# make wasi-test    → run just the WASI integration tests
# make android      → build MAUI Android package (requires MAUI workload)
# make ios          → build MAUI iOS package (requires Mac + Xcode)
# make test         → run all tests
# make wit-gen      → regenerate C# bindings from .wit file
# make clean        → clean all build artifacts
# ──────────────────────────────────────────────────────────────────────────────

DOTNET            := dotnet
WASMTIME          := wasmtime
WIT_BINDGEN       := wit-bindgen
CORE_PROJECT      := src/BlazorNative.Core/BlazorNative.Core.csproj
RENDERER_PROJECT  := src/BlazorNative.Renderer/BlazorNative.Renderer.csproj
HTTP_PROJECT      := src/BlazorNative.Http/BlazorNative.Http.csproj
ANALYZER_PROJECT  := src/BlazorNative.Analyzers/BlazorNative.Analyzers.csproj
DEV_PROJECT       := src/BlazorNative.Host.Android/BlazorNative.DevHost.csproj
WASI_PROJECT      := src/BlazorNative.WasiHost/BlazorNative.WasiHost.csproj
WASI_TESTS        := tests/BlazorNative.Wasi.Tests/BlazorNative.Wasi.Tests.csproj
# Mono-AOT for wasi-wasm drops the app-specific .wasm at AppBundle/<AppName>.wasm.
# This is NOT in any --output dir — passing --output to dotnet publish would only
# capture the IL .dlls + the generic dotnet.wasm runtime.
WASI_APP_BUNDLE   := src/BlazorNative.WasiHost/bin/Release/net10.0/wasi-wasm/AppBundle
WASI_APP_WASM     := $(WASI_APP_BUNDLE)/BlazorNative.WasiHost.wasm

.PHONY: dev dev-no-reload wasi wasi-run wasi-test wasi-inspect android android-build android-test android-test-visible ios test test-watch wit-gen clean setup analyzers help

## ── Development ──────────────────────────────────────────────────────────────

dev:                          ## Start dev host with hot reload
	@echo "🚀 Starting BlazorNative DevHost..."
	@echo "   App:      https://localhost:5273"
	@echo "   DevTools: https://localhost:5273/dev/storage"
	$(DOTNET) watch run --project $(DEV_PROJECT)

dev-no-reload:                ## Start dev host without hot reload
	$(DOTNET) run --project $(DEV_PROJECT)

## ── WASI compilation ─────────────────────────────────────────────────────────

wasi:                         ## Publish WasiHost to WASM/WASI (Mono-AOT)
	@echo "🔧 Publishing BlazorNative.WasiHost to wasi-wasm (Release)..."
	$(DOTNET) publish $(WASI_PROJECT) -r wasi-wasm -c Release
	@echo "✅ WASM output: $(WASI_APP_WASM)"
	@ls -lh $(WASI_APP_WASM) 2>/dev/null || echo "  (no .wasm produced — check publish output above)"

wasi-run: wasi                ## Publish + run via wasmtime
	@echo "▶️  Running BlazorNative.WasiHost.wasm via wasmtime..."
	@echo "    (cd into AppBundle so --dir=. stays relative — absolute paths break Mono ICU lookup)"
	cd $(WASI_APP_BUNDLE) && $(WASMTIME) run -Shttp --dir=. BlazorNative.WasiHost.wasm

wasi-test:                    ## Run just the WASI integration tests (publishes via fixture)
	@echo "🧪 Running WASI integration tests..."
	$(DOTNET) test $(WASI_TESTS) --logger "console;verbosity=normal"

wasi-inspect: wasi            ## Show WASM module imports/exports
	@echo "🔍 Module interface:"
	$(WASMTIME) wasm-tools dump $(WASI_APP_WASM) | grep -E "(import|export)"

analyzers:                    ## Build Roslyn analyzers
	$(DOTNET) build $(ANALYZER_PROJECT) -c Release

## ── Mobile targets ───────────────────────────────────────────────────────────

android-build:                ## Build the Phase 2.2 Android APK (BlazorNative.Jni)
	@echo "🚀 Building Android APK (assembleDebug)..."
	cd src/BlazorNative.Jni && JAVA_HOME="C:/Program Files/Eclipse Adoptium/jdk-21.0.11.10-hotspot" ANDROID_HOME="$$USERPROFILE/AppData/Local/Android/Sdk" ./gradlew.bat assembleDebug --no-daemon

android-test:                 ## Boot AVD if needed + run connectedAndroidTest
	powershell -ExecutionPolicy Bypass -File scripts/test-android.ps1

android-test-visible:         ## Same as android-test but with visible emulator window
	powershell -ExecutionPolicy Bypass -File scripts/test-android.ps1 -ShowEmulator

android:                      ## Legacy alias — same as android-build
	@$(MAKE) android-build

ios:                          ## Build iOS IPA (requires Mac + Xcode)
	$(DOTNET) publish src/BlazorNative.Host.Android/BlazorNative.DevHost.csproj \
		-f net10.0-ios \
		-c Release \
		-o artifacts/ios

## ── WIT tooling ──────────────────────────────────────────────────────────────

wit-gen:                      ## Regenerate bindings from .wit file
	@echo "⚙️  Generating bindings from WIT..."
	$(WIT_BINDGEN) generate --language csharp \
		tools/wit/mobile-bridge.wit \
		--out-dir src/BlazorNative.Bridge/Generated
	$(WIT_BINDGEN) generate --language kotlin \
		tools/wit/mobile-bridge.wit \
		--out-dir src/BlazorNative.Host.Android/Generated
	@echo "✅ Bindings generated"

## ── Testing ──────────────────────────────────────────────────────────────────

test:                         ## Run all tests
	$(DOTNET) test --logger "console;verbosity=normal"

test-watch:                   ## Run tests with file watcher
	$(DOTNET) watch test

## ── Utilities ────────────────────────────────────────────────────────────────

setup:                        ## Install required prerequisites
ifeq ($(OS),Windows_NT)
	@echo "📦 On Windows, run the canonical installer (admin shell):"
	@echo "    powershell -ExecutionPolicy Bypass -File setup.ps1"
	@echo ""
	@echo "It installs .NET 10 SDK, wasi-experimental workload, wasi-sdk-25,"
	@echo "wasmtime v45, MAUI Android, OpenJDK 17, and Android SDK."
else
	@echo "📦 Installing .NET workloads..."
	$(DOTNET) workload install wasi-experimental
	$(DOTNET) workload install maui-android
	$(DOTNET) workload install maui-ios
	@echo "✅ Workloads installed"
	@echo ""
	@echo "You also need:"
	@echo "  • wasi-sdk 25 → https://github.com/WebAssembly/wasi-sdk/releases/tag/wasi-sdk-25"
	@echo "    (set WASI_SDK_PATH to its extracted root)"
	@echo "  • wasmtime v45 → https://github.com/bytecodealliance/wasmtime/releases/tag/v45.0.0"
endif

clean:                        ## Clean all build artifacts
	$(DOTNET) clean
	rm -rf artifacts
	find . -name "bin" -o -name "obj" | xargs rm -rf

help:                         ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*##' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*##"}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'
