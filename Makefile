# BlazorNative — Developer Makefile
# ──────────────────────────────────────────────────────────────────────────────
# make dev          → start dev host with hot reload (normal .NET, no WASM)
# make wasi         → compile core to WASM/WASI
# make wasi-run     → compile + run via wasmtime
# make android      → build MAUI Android package (requires MAUI workload)
# make ios          → build MAUI iOS package (requires Mac + Xcode)
# make test         → run all tests
# make wit-gen      → regenerate C# bindings from .wit file
# make clean        → clean all build artifacts
# ──────────────────────────────────────────────────────────────────────────────

DOTNET        := dotnet
WASMTIME      := wasmtime
WIT_BINDGEN   := wit-bindgen
CORE_PROJECT      := src/BlazorNative.Core/BlazorNative.Core.csproj
RENDERER_PROJECT  := src/BlazorNative.Renderer/BlazorNative.Renderer.csproj
HTTP_PROJECT      := src/BlazorNative.Http/BlazorNative.Http.csproj
ANALYZER_PROJECT  := src/BlazorNative.Analyzers/BlazorNative.Analyzers.csproj
DEV_PROJECT       := src/BlazorNative.Host.Android/BlazorNative.DevHost.csproj
WASI_OUT      := artifacts/wasi

.PHONY: dev wasi wasi-run android ios test wit-gen clean setup help

## ── Development ──────────────────────────────────────────────────────────────

dev:                          ## Start dev host with hot reload
	@echo "🚀 Starting BlazorNative DevHost..."
	@echo "   App:      https://localhost:5273"
	@echo "   DevTools: https://localhost:5273/dev/storage"
	$(DOTNET) watch run --project $(DEV_PROJECT)

dev-no-reload:                ## Start dev host without hot reload
	$(DOTNET) run --project $(DEV_PROJECT)

## ── WASI compilation ─────────────────────────────────────────────────────────

wasi:                         ## Compile core library to WASM/WASI
	@echo "🔧 Building WASI target..."
	@mkdir -p $(WASI_OUT)
	$(DOTNET) build $(CORE_PROJECT) \
		-r wasi-wasm \
		-c Release \
		--output $(WASI_OUT)
	@echo "✅ WASM output: $(WASI_OUT)"
	@ls -lh $(WASI_OUT)/*.wasm 2>/dev/null || echo "  (no .wasm yet — add a WASI entrypoint)"

wasi-run: wasi                ## Compile + run via wasmtime
	@echo "▶️  Running via wasmtime..."
	$(WASMTIME) $(WASI_OUT)/BlazorNative.Core.wasm

analyzers:                    ## Build Roslyn analyzers
	$(DOTNET) build $(ANALYZER_PROJECT) -c Release

wasi-inspect: wasi            ## Show WASM module imports/exports
	@echo "🔍 Module interface:"
	$(WASMTIME) wasm-tools dump $(WASI_OUT)/BlazorNative.Core.wasm | grep -E "(import|export)"

## ── Mobile targets ───────────────────────────────────────────────────────────

android:                      ## Build Android APK via MAUI
	$(DOTNET) publish src/BlazorNative.Host.Android/BlazorNative.DevHost.csproj \
		-f net9.0-android \
		-c Release \
		-o artifacts/android

ios:                          ## Build iOS IPA (requires Mac + Xcode)
	$(DOTNET) publish src/BlazorNative.Host.Android/BlazorNative.DevHost.csproj \
		-f net9.0-ios \
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

setup:                        ## Install required .NET workloads
	@echo "📦 Installing workloads..."
	$(DOTNET) workload install wasi-experimental
	$(DOTNET) workload install maui-android
	$(DOTNET) workload install maui-ios
	@echo "✅ Workloads installed"
	@echo ""
	@echo "You also need wasmtime: https://wasmtime.dev"
	@echo "  curl https://wasmtime.dev/install.sh -sSf | bash"

clean:                        ## Clean all build artifacts
	$(DOTNET) clean
	rm -rf artifacts
	find . -name "bin" -o -name "obj" | xargs rm -rf

help:                         ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*##' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*##"}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'
