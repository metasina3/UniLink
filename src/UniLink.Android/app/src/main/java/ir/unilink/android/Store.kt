package ir.unilink.android

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONObject
import java.io.File
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** AES-256-GCM with a non-exportable key held in the Android Keystore. */
private object Crypto {
    private const val ALIAS = "unilink_store_key"
    private const val TRANSFORM = "AES/GCM/NoPadding"
    private const val IV_LEN = 12

    private fun key(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(
            KeyGenParameterSpec.Builder(
                ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build()
        )
        return gen.generateKey()
    }

    fun encrypt(plain: ByteArray): ByteArray {
        val c = Cipher.getInstance(TRANSFORM)
        c.init(Cipher.ENCRYPT_MODE, key())
        val ct = c.doFinal(plain)
        return c.iv + ct
    }

    fun decrypt(blob: ByteArray): ByteArray {
        val c = Cipher.getInstance(TRANSFORM)
        c.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, blob, 0, IV_LEN))
        return c.doFinal(blob, IV_LEN, blob.size - IV_LEN)
    }
}

/** Encrypted, atomically-written settings store exposed as a StateFlow. */
class Store(private val ctx: Context) {
    private val file = File(ctx.filesDir, "unilink.dat")
    private val backup = File(ctx.filesDir, "unilink.bak")
    private val temp = File(ctx.filesDir, "unilink.tmp")

    private val _data = MutableStateFlow(load())
    val data: StateFlow<AppData> = _data.asStateFlow()

    @Synchronized
    fun update(block: (AppData) -> AppData) {
        val next = block(_data.value)
        if (next != _data.value) {
            _data.value = next
            save(next)
        }
    }

    fun updateGroup(id: String, block: (NetworkGroup) -> NetworkGroup) = update { d ->
        d.copy(groups = d.groups.map { if (it.id == id) block(it) else it })
    }

    fun updateAccount(groupId: String, accountId: String, block: (Account) -> Account) =
        updateGroup(groupId) { g ->
            g.copy(accounts = g.accounts.map { if (it.id == accountId) block(it) else it })
        }

    fun upsertAccount(groupId: String, account: Account) = updateGroup(groupId) { g ->
        if (g.accounts.any { it.id == account.id })
            g.copy(accounts = g.accounts.map { if (it.id == account.id) account else it })
        else g.copy(accounts = g.accounts + account)
    }

    fun upsertGroup(group: NetworkGroup) = update { d ->
        if (d.groups.any { it.id == group.id })
            d.copy(groups = d.groups.map { if (it.id == group.id) group else it })
        else d.copy(groups = d.groups + group)
    }

    fun deleteGroup(id: String) = update { d -> d.copy(groups = d.groups.filterNot { it.id == id }) }

    fun updateSettings(block: (Settings) -> Settings) = update { d -> d.copy(settings = block(d.settings)) }

    private fun load(): AppData {
        for (f in listOf(file, backup)) {
            try {
                if (f.exists()) {
                    val json = String(Crypto.decrypt(f.readBytes()), Charsets.UTF_8)
                    return appDataFromJson(JSONObject(json))
                }
            } catch (_: Exception) {
                // try the backup, then fall back to defaults
            }
        }
        return AppData.default()
    }

    private fun save(d: AppData) {
        try {
            val blob = Crypto.encrypt(d.toJson().toString().toByteArray(Charsets.UTF_8))
            temp.writeBytes(blob)
            if (file.exists()) file.copyTo(backup, overwrite = true)
            if (!temp.renameTo(file)) {
                file.writeBytes(blob)
                temp.delete()
            }
        } catch (_: Exception) {
            // Keep running with the in-memory copy if storage fails.
        }
    }
}
