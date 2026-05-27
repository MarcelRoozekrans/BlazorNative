package io.blazornative.jni

import org.junit.jupiter.api.Test
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue

class FrameStreamParserTest {

    @Test
    fun parses_FRAME_lines_and_ignores_others() {
        val stdout = """
            [BOOT] runtime-start
            [BOOT] di-ok bridge=WasiBridge renderer=NativeRenderer
            [FRAME] {"frameId":1,"timestampMs":100,"patches":[{"op":"commit","frameId":1,"timestampMs":100}]}
            [BOOT] done
            [FRAME] {"frameId":2,"timestampMs":200,"patches":[{"op":"commit","frameId":2,"timestampMs":200}]}
            stray text without prefix
        """.trimIndent()

        val frames = FrameStreamParser.parse(stdout)

        assertEquals(2, frames.size)
        assertEquals(1, frames[0].frameId)
        assertEquals(2, frames[1].frameId)
    }

    @Test
    fun returns_empty_list_when_no_FRAME_lines() {
        val stdout = "[BOOT] runtime-start\n[BOOT] done"
        assertTrue(FrameStreamParser.parse(stdout).isEmpty())
    }

    @Test
    fun handles_empty_input() {
        assertTrue(FrameStreamParser.parse("").isEmpty())
    }
}
