package ir.unilink.android

import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

// ---------- JSON ----------

private fun JSONArray.strings(): List<String> = (0 until length()).map { getString(it) }
private fun List<String>.toJson() = JSONArray().also { a -> forEach { a.put(it) } }

fun Account.toJson() = JSONObject()
    .put("id", id).put("name", name).put("username", username).put("password", password)
    .put("enabled", enabled).put("exhaustedMonth", exhaustedMonth ?: "")
    .put("exhaustedAt", exhaustedAt).put("passwordRejected", passwordRejected)
    .apply { usage?.let { put("usage", it.toJson()) } }

fun UsageInfo.toJson() = JSONObject()
    .put("allotted", allottedMb ?: JSONObject.NULL).put("used", usedMb)
    .put("remaining", remainingMb ?: JSONObject.NULL)
    .put("renewal", renewal).put("at", fetchedAt)

fun usageFromJson(o: JSONObject) = UsageInfo(
    allottedMb = if (o.isNull("allotted")) null else o.optDouble("allotted"),
    usedMb = o.optDouble("used", 0.0),
    remainingMb = if (o.isNull("remaining")) null else o.optDouble("remaining"),
    renewal = o.optString("renewal", ""),
    fetchedAt = o.optLong("at", 0L),
)

fun accountFromJson(o: JSONObject) = Account(
    id = o.optString("id").ifEmpty { UUID.randomUUID().toString() },
    name = o.optString("name"),
    username = o.optString("username"),
    password = o.optString("password"),
    enabled = o.optBoolean("enabled", true),
    exhaustedMonth = o.optString("exhaustedMonth", "").ifEmpty { null },
    exhaustedAt = o.optLong("exhaustedAt", 0L),
    passwordRejected = o.optBoolean("passwordRejected", false),
    usage = o.optJSONObject("usage")?.let { usageFromJson(it) },
)

fun NetworkGroup.toJson(): JSONObject {
    val pinObj = JSONObject()
    pins.forEach { (k, v) -> pinObj.put(k, v) }
    val accs = JSONArray()
    accounts.forEach { accs.put(it.toJson()) }
    return JSONObject()
        .put("id", id).put("name", name)
        .put("ssids", ssids.toJson()).put("portalUrls", portalUrls.toJson())
        .put("portalOnly", portalOnly).put("keepaliveSec", keepaliveSec)
        .put("timeZone", timeZone).put("quotaPhrases", quotaPhrases.toJson())
        .put("pins", pinObj).put("accounts", accs).put("usageUrl", usageUrl)
}

fun groupFromJson(o: JSONObject): NetworkGroup {
    val pinObj = o.optJSONObject("pins") ?: JSONObject()
    val pins = pinObj.keys().asSequence().associateWith { pinObj.getString(it) }
    val accArr = o.optJSONArray("accounts") ?: JSONArray()
    val portalUrls = (o.optJSONArray("portalUrls") ?: JSONArray()).strings()
    // Groups saved before 1.10.0 have no usageUrl: enable it for the Urmia University portal only.
    val usageUrl = if (o.has("usageUrl")) o.optString("usageUrl")
    else if (portalUrls.any { it.contains("192.168.100.40") }) NetworkGroup.URMIA_USAGE_URL else ""
    return NetworkGroup(
        id = o.optString("id").ifEmpty { UUID.randomUUID().toString() },
        name = o.optString("name"),
        ssids = (o.optJSONArray("ssids") ?: JSONArray()).strings(),
        portalUrls = portalUrls,
        usageUrl = usageUrl,
        portalOnly = o.optBoolean("portalOnly", false),
        keepaliveSec = o.optInt("keepaliveSec", 180),
        timeZone = o.optString("timeZone", NetworkGroup.DEFAULT_TZ),
        quotaPhrases = (o.optJSONArray("quotaPhrases") ?: JSONArray()).strings(),
        pins = pins,
        accounts = (0 until accArr.length()).map { accountFromJson(accArr.getJSONObject(it)) },
    )
}

fun AppData.toJson(): JSONObject {
    val g = JSONArray()
    groups.forEach { g.put(it.toJson()) }
    return JSONObject().put("version", 1).put("groups", g).put(
        "settings",
        JSONObject()
            .put("autoSignIn", settings.autoSignIn)
            .put("startOnBoot", settings.startOnBoot)
            .put("holdWakeLock", settings.holdWakeLock)
            .put("holdWifiLock", settings.holdWifiLock)
            .put("updateNotify", settings.updateNotify)
            .put("notifiedVersion", settings.notifiedVersion)
            .put("lastUpdateCheck", settings.lastUpdateCheck)
            .put("preferMostData", settings.preferMostData)
            .put("notifyProblems", settings.notifyProblems)
            .put("switchOnUse", settings.switchOnUse)
            .put("tourDone", settings.tourDone)
    )
}

fun appDataFromJson(o: JSONObject): AppData {
    val arr = o.optJSONArray("groups") ?: JSONArray()
    val s = o.optJSONObject("settings") ?: JSONObject()
    return AppData(
        groups = (0 until arr.length()).map { groupFromJson(arr.getJSONObject(it)) },
        settings = Settings(
            autoSignIn = s.optBoolean("autoSignIn", true),
            startOnBoot = s.optBoolean("startOnBoot", true),
            holdWakeLock = s.optBoolean("holdWakeLock", true),
            holdWifiLock = s.optBoolean("holdWifiLock", true),
            updateNotify = s.optBoolean("updateNotify", true),
            notifiedVersion = s.optString("notifiedVersion", ""),
            lastUpdateCheck = s.optLong("lastUpdateCheck", 0L),
            preferMostData = s.optBoolean("preferMostData", false),
            notifyProblems = s.optBoolean("notifyProblems", true),
            switchOnUse = s.optBoolean("switchOnUse", true),
            // Existing installs have already seen the app: do not show them the first-run tour.
            tourDone = s.optBoolean("tourDone", s.has("autoSignIn")),
        ),
    )
}
