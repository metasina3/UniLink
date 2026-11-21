package ir.unilink.android

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class UsageParserTest {
    private fun fixture(): String =
        javaClass.classLoader!!.getResourceAsStream("account_status.html")!!.readBytes().toString(Charsets.UTF_8)

    @Test
    fun readsTheMonthlyAllowanceFromARealPage() {
        val info = assertNotNull(UsageParser.parse(fixture(), now = 1L))
        assertEquals(12000.0, info.allottedMb)
        assertEquals(9843.63, info.usedMb)
        assertEquals(2156.37, info.remainingMb)
        assertEquals("01/12/2026", info.renewal)
        assertEquals(1L, info.fetchedAt)
    }

    @Test
    fun usageFractionIsUsedOverAllowed() {
        val info = assertNotNull(UsageParser.parse(fixture(), now = 0L))
        val f = assertNotNull(info.fraction)
        assertEquals(0.82, f.toDouble(), 0.01)
    }

    @Test
    fun acceptsBothQuoteStylesForRowIds() {
        val single = fixture()
        val double = single.replace("id='", "id=\"").replace("'>", "\">")
        assertEquals(UsageParser.parse(single, 0L)?.usedMb, UsageParser.parse(double, 0L)?.usedMb)
        assertNotNull(UsageParser.parse(double, 0L))
    }

    @Test
    fun unlimitedAccountHasNoAllowance() {
        val html = fixture().replace("12000&nbsp;<label id='Language.MB'></label>", "<label id='Language.NAWithDot'></label>")
        val info = assertNotNull(UsageParser.parse(html, 0L))
        assertNull(info.allottedMb)
        assertNull(info.fraction)
    }

    @Test
    fun pageWithoutTablesGivesNull() {
        assertNull(UsageParser.parse("<html><body>Http Error:404</body></html>", 0L))
    }

    @Test
    fun convertsUnitsToMegabytes() {
        assertEquals(12000.0, UsageParser.megabytes("12000 MB"))
        assertEquals(1536.0, UsageParser.megabytes("1.5 GB"))
        assertEquals(1.0, UsageParser.megabytes("1024 KB"))
        assertEquals(1234.5, UsageParser.megabytes("1,234.5"))
        assertNull(UsageParser.megabytes("N.A."))
        assertNull(UsageParser.megabytes(""))
    }

    @Test
    fun detectsTheLoginPage() {
        assertTrue(UsageParser.looksLikeLogin("<input name=\"username\"><input name=\"password\">"))
        assertFalse(UsageParser.looksLikeLogin(fixture()))
    }
}

class PortalParserTest {
    private fun xml(status: String, message: String) =
        "<?xml version=\"1.0\"?><requestresponse><status><![CDATA[$status]]></status>" +
            "<message><![CDATA[$message]]></message></requestresponse>"

    @Test
    fun liveStatusIsSuccess() {
        val r = PortalParser.classify(xml("LIVE", "You are signed in as x"), isLogin = true, extraQuotaPhrases = emptyList())
        assertTrue(r is PortalResult.Success)
    }

    @Test
    fun quotaPhraseIsDetected() {
        val r = PortalParser.classify(xml("Rejected", "Your data transfer has been exceeded"), true, emptyList())
        assertTrue(r is PortalResult.Quota)
    }

    @Test
    fun customQuotaPhraseIsDetected() {
        val r = PortalParser.classify(xml("Rejected", "monthly limit reached"), true, listOf("monthly limit"))
        assertTrue(r is PortalResult.Quota)
    }

    @Test
    fun wrongPasswordIsDetected() {
        val r = PortalParser.classify(xml("Rejected", "The system could not log you on. Make sure your password is correct"), true, emptyList())
        assertTrue(r is PortalResult.BadCredentials)
    }

    @Test
    fun liveStatusBeatsQuotaWordsInTheMessage() {
        val r = PortalParser.classify(xml("LIVE", "Your data transfer has been exceeded soon"), true, emptyList())
        assertTrue(r is PortalResult.Success)
    }

    @Test
    fun keepaliveAckIsSuccess() {
        val r = PortalParser.classify("<liverequestresponse><ack><![CDATA[ack]]></ack></liverequestresponse>", false, emptyList())
        assertTrue(r is PortalResult.Success)
    }

    @Test
    fun nonPortalReplyIsFailure() {
        val r = PortalParser.classify("<html>captive page</html>", true, emptyList())
        assertTrue(r is PortalResult.Failure)
    }
}

class VersionsTest {
    @Test
    fun comparesVersionsNumerically() {
        assertTrue(Versions.isNewer("v1.10.0", "1.9.2"))
        assertTrue(Versions.isNewer("1.11.0", "1.10.6"))
        assertFalse(Versions.isNewer("v1.10.6", "1.10.6"))
        assertFalse(Versions.isNewer("1.9.2", "1.10.0"))
        assertTrue(Versions.isNewer("2.0", "1.99.99"))
    }
}

class LoginOrderTest {
    private val now = 1_000_000_000_000L
    private fun acc(name: String, left: Double?, age: Long = 0L, unlimited: Boolean = false) = Account(
        id = name, name = name, username = name, password = "x",
        usage = if (left == null && !unlimited) null
        else UsageInfo(if (unlimited) null else 12000.0, 0.0, if (unlimited) null else left, "", now - age),
    )

    @Test
    fun keepsListOrderWhenThePreferenceIsOff() {
        val list = listOf(acc("a", 500.0), acc("b", 9000.0), acc("c", null))
        assertEquals(listOf("a", "b", "c"), LoginOrder.order(list, now, false).map { it.name })
    }

    @Test
    fun nearlyEmptyAccountsAlwaysGoLast() {
        val list = listOf(acc("a", 50.0), acc("b", 9000.0), acc("c", null))
        assertEquals(listOf("b", "c", "a"), LoginOrder.order(list, now, false).map { it.name })
    }

    @Test
    fun mostDataFirstWhenEnabled() {
        val list = listOf(acc("a", 2000.0), acc("b", 9000.0), acc("c", 5000.0))
        assertEquals(listOf("b", "c", "a"), LoginOrder.order(list, now, true).map { it.name })
    }

    @Test
    fun staleReadingsAreIgnored() {
        val old = LoginOrder.FRESH_MS + 1
        val list = listOf(acc("a", 10.0, age = old), acc("b", 9000.0))
        // "a" looks empty but the reading is stale, so it keeps its place instead of going last.
        assertEquals(listOf("a", "b"), LoginOrder.order(list, now, false).map { it.name })
    }

    @Test
    fun unlimitedBeatsFiniteAndUnknownSitsBetween() {
        val list = listOf(acc("a", 9000.0), acc("u", null, unlimited = true), acc("k", null))
        assertEquals(listOf("u", "k", "a"), LoginOrder.order(list, now, true).map { it.name })
    }

    @Test
    fun formatsSizes() {
        assertEquals("840 MB", formatMb(840.0))
        assertEquals("1.5 GB", formatMb(1536.0))
    }
}
