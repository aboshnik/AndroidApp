package com.example.app

import android.content.Intent
import android.content.ActivityNotFoundException
import android.net.Uri
import android.os.Bundle
import android.os.Build
import android.util.Log
import androidx.appcompat.app.AlertDialog
import android.widget.ImageView
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.FileProvider
import android.provider.Settings
import okhttp3.OkHttpClient
import okhttp3.Request
import com.example.app.api.ApiClient
import java.io.File
import java.util.Locale
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class ProfileActivity : AppCompatActivity() {
    private val scopeJob = Job()
    private val scope = CoroutineScope(Dispatchers.Main + scopeJob)
    private val prefs by lazy { getSharedPreferences("profile", MODE_PRIVATE) }
    private val authPrefs by lazy { getSharedPreferences("auth", MODE_PRIVATE) }
    private val logTag = "ProfileActivityNet"

    private fun canShowUi(): Boolean = !(isFinishing || isDestroyed)

    private fun safeToast(text: String, long: Boolean = false) {
        if (!canShowUi()) return
        Toast.makeText(this, text, if (long) Toast.LENGTH_LONG else Toast.LENGTH_SHORT).show()
    }

    private fun safeShowDialog(builder: AlertDialog.Builder): AlertDialog? {
        if (!canShowUi()) return null
        return try {
            builder.show()
        } catch (_: android.view.WindowManager.BadTokenException) {
            null
        }
    }

    private val pickAvatar = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) {
            val flags = android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION
            try {
                contentResolver.takePersistableUriPermission(uri, flags)
            } catch (_: Exception) {
            }
            prefs.edit().putString("avatar_uri", uri.toString()).apply()
            findViewById<ImageView>(R.id.ivAvatar).setImageURI(uri)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_profile)

        val lastNameFallback = (intent.getStringExtra("lastName") ?: authPrefs.getString("lastName", "")).orEmpty()
        val firstNameFallback = (intent.getStringExtra("firstName") ?: authPrefs.getString("firstName", "")).orEmpty()
        val phoneFallback = (intent.getStringExtra("phone") ?: authPrefs.getString("phone", "")).orEmpty()

        val tvName = findViewById<TextView>(R.id.tvName)
        val tvEmployeeId = findViewById<TextView>(R.id.tvEmployeeId)
        val tvPhone = findViewById<TextView>(R.id.tvPhone)
        val tvPosition = findViewById<TextView>(R.id.tvPosition)
        val tvSubdivision = findViewById<TextView>(R.id.tvSubdivision)

        if (lastNameFallback.isNotBlank() || firstNameFallback.isNotBlank()) {
            tvName.text = "${lastNameFallback} ${firstNameFallback}".trim()
        }
        if (phoneFallback.isNotBlank()) {
            tvPhone.text = phoneFallback
        }

        val avatarView = findViewById<ImageView>(R.id.ivAvatar)
        prefs.getString("avatar_uri", null)?.let { saved ->
            runCatching { avatarView.setImageURI(android.net.Uri.parse(saved)) }
        }
        avatarView.setOnClickListener {
            pickAvatar.launch(arrayOf("image/*"))
        }

        findViewById<android.view.View>(R.id.rowNotificationSettings).setOnClickListener {
            openNotificationSettingsDialog()
        }

        findViewById<android.view.View>(R.id.rowAppUpdate).setOnClickListener {
            checkAndUpdateApp()
        }

        findViewById<android.view.View>(R.id.rowLogout).setOnClickListener {
            confirmLogout()
        }
        val rowDeveloperConsole = findViewById<android.view.View>(R.id.rowDeveloperConsole)
        val canUseDevConsole = intent.getBooleanExtra("canUseDevConsole", authPrefs.getBoolean("canUseDevConsole", false))
        rowDeveloperConsole.visibility = if (canUseDevConsole) android.view.View.VISIBLE else android.view.View.GONE
        rowDeveloperConsole.setOnClickListener {
            startActivity(Intent(this, DeveloperConsoleActivity::class.java))
        }

        setupBottomNav()

        val employeeId = (intent.getStringExtra("employeeId") ?: authPrefs.getString("employeeId", null))
        val login = (intent.getStringExtra("login") ?: authPrefs.getString("login", null))

        scope.launch {
            try {
                val response = withContext(Dispatchers.IO) {
                    ApiClient.employeeApi.getProfile(employeeId = employeeId, login = login)
                }
                val body = response.body()

                if (!response.isSuccessful || body == null || !body.success || body.profile == null) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }

                val p = body.profile
                tvName.text = "${p.lastName} ${p.firstName}".trim()
                tvPhone.text = p.phone
                tvEmployeeId.text = p.employeeId
                tvPosition.text = p.position
                tvSubdivision.text = p.subdivision
            } catch (e: Exception) {
                val details = buildErrorDetails(e)
                Log.e(logTag, details, e)
                safeToast("${getString(R.string.error_network)} $details", long = true)
                showNetworkErrorDialog(details)
            }
        }
    }

    override fun onResume() {
        super.onResume()
        SessionManager.touch(this)
    }

    override fun onDestroy() {
        super.onDestroy()
        scopeJob.cancel()
    }

    private fun setupBottomNav() {
        findViewById<android.view.View>(R.id.navHome).setOnClickListener {
            val i = Intent(this, HomeActivity::class.java)
            i.putExtra("employeeId", intent.getStringExtra("employeeId") ?: authPrefs.getString("employeeId", "") ?: "")
            i.putExtra("login", intent.getStringExtra("login") ?: authPrefs.getString("login", "") ?: "")
            i.putExtra("lastName", intent.getStringExtra("lastName") ?: authPrefs.getString("lastName", "") ?: "")
            i.putExtra("firstName", intent.getStringExtra("firstName") ?: authPrefs.getString("firstName", "") ?: "")
            i.putExtra("phone", intent.getStringExtra("phone") ?: authPrefs.getString("phone", "") ?: "")
            i.putExtra("canCreatePosts", intent.getBooleanExtra("canCreatePosts", authPrefs.getBoolean("canCreatePosts", false)))
            i.putExtra("canUseDevConsole", intent.getBooleanExtra("canUseDevConsole", authPrefs.getBoolean("canUseDevConsole", false)))
            startActivity(i)
            finish()
        }
        findViewById<android.view.View>(R.id.navCalendar).setOnClickListener {
            val i = Intent(this, CalendarActivity::class.java)
            i.putExtra("employeeId", intent.getStringExtra("employeeId") ?: authPrefs.getString("employeeId", "") ?: "")
            i.putExtra("login", intent.getStringExtra("login") ?: authPrefs.getString("login", "") ?: "")
            i.putExtra("lastName", intent.getStringExtra("lastName") ?: authPrefs.getString("lastName", "") ?: "")
            i.putExtra("firstName", intent.getStringExtra("firstName") ?: authPrefs.getString("firstName", "") ?: "")
            i.putExtra("phone", intent.getStringExtra("phone") ?: authPrefs.getString("phone", "") ?: "")
            i.putExtra("canCreatePosts", intent.getBooleanExtra("canCreatePosts", authPrefs.getBoolean("canCreatePosts", false)))
            i.putExtra("canUseDevConsole", intent.getBooleanExtra("canUseDevConsole", authPrefs.getBoolean("canUseDevConsole", false)))
            startActivity(i)
        }
        findViewById<android.view.View>(R.id.navProfile).setOnClickListener { }
    }

    override fun onSupportNavigateUp(): Boolean {
        finish()
        return true
    }

    private fun openNotificationSettingsDialog() {
        val labels = arrayOf(
            getString(R.string.notif_type_post),
            getString(R.string.notif_type_security),
            getString(R.string.notif_type_test)
        )
        val checked = NotificationSettingsManager.getStates(this).copyOf()

        safeShowDialog(
            AlertDialog.Builder(this)
                .setTitle(getString(R.string.notif_settings_title))
                .setMultiChoiceItems(labels, checked) { _, which, isChecked ->
                    if (which in checked.indices) checked[which] = isChecked
                }
                .setPositiveButton(android.R.string.ok) { _, _ ->
                    NotificationSettingsManager.saveStates(this, checked)
                    safeToast(getString(R.string.notif_settings_saved))
                }
                .setNegativeButton(android.R.string.cancel, null)
        )
    }

    private fun confirmLogout() {
        safeShowDialog(
            AlertDialog.Builder(this)
                .setTitle(getString(R.string.logout_title))
                .setMessage(getString(R.string.logout_message))
                .setPositiveButton(getString(R.string.logout_button)) { _, _ ->
                    logoutNow()
                }
                .setNegativeButton(android.R.string.cancel, null)
        )
    }

    private fun logoutNow() {
        SessionManager.clear(this)
        val i = Intent(this, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        }
        startActivity(i)
        finish()
    }

    private fun checkAndUpdateApp() {
        val currentVersionCode = BuildConfig.VERSION_CODE

        scope.launch {
            try {
                val response = withContext(Dispatchers.IO) {
                    ApiClient.appUpdateApi.getLatest()
                }
                val body = response.body()
                if (!response.isSuccessful || body == null) {
                    safeToast(getString(R.string.error_network), long = true)
                    return@launch
                }

                val latestVersion = body.versionCode
                val apkUrl = body.apkUrl

                if (apkUrl.isNullOrBlank() || latestVersion <= currentVersionCode) {
                    safeToast("У вас актуальная версия приложения")
                    return@launch
                }

                safeShowDialog(
                    AlertDialog.Builder(this@ProfileActivity)
                        .setTitle("Обновление приложения")
                        .setMessage("Доступна новая версия. Обновить сейчас?")
                        .setPositiveButton("Обновить") { _, _ ->
                            scope.launch {
                                beginDownloadAndInstall(apkUrl, latestVersion)
                            }
                        }
                        .setNegativeButton(android.R.string.cancel, null)
                )
            } catch (e: Exception) {
                val details = buildErrorDetails(e)
                Log.e(logTag, details, e)
                safeToast("${getString(R.string.error_network)} $details", long = true)
                showNetworkErrorDialog(details)
            }
        }
    }

    private fun buildErrorDetails(e: Throwable): String {
        var t: Throwable? = e
        val chain = mutableListOf<String>()
        var guard = 0
        while (t != null && guard < 5) {
            val name = t.javaClass.simpleName
            val msg = t.message?.trim().orEmpty()
            chain += if (msg.isNotBlank()) "$name: $msg" else name
            t = t.cause
            guard++
        }
        return chain.joinToString(" -> ")
    }

    private fun showNetworkErrorDialog(details: String) {
        safeShowDialog(
            AlertDialog.Builder(this)
                .setTitle("Подробности ошибки сети")
                .setMessage(details)
                .setPositiveButton("OK", null)
        )
    }

    private suspend fun beginDownloadAndInstall(apkUrl: String, versionCode: Int) {
        if (!ensureUnknownSourcesInstallAllowed()) {
            safeToast("Разрешите установку из неизвестных источников и нажмите ещё раз.", long = true)
            return
        }

        val progress = safeShowDialog(
            AlertDialog.Builder(this)
                .setTitle("Обновление приложения")
                .setMessage("Скачивание обновления…")
                .setCancelable(false)
        )

        try {
            val apkFile = downloadApkToCache(apkUrl, versionCode)
            val mb = (apkFile.length().toDouble() / (1024.0 * 1024.0))
            progress?.setMessage(String.format(Locale("ru"), "Скачано %.2f МБ. Запуск установки…", mb))
            installApkFile(apkFile)
        } catch (e: Exception) {
            val details = buildErrorDetails(e)
            Log.e(logTag, "Update failed: $details", e)
            safeToast("Ошибка обновления: $details", long = true)
            showNetworkErrorDialog(details)
        } finally {
            runCatching { progress?.dismiss() }
        }
    }

    private fun ensureUnknownSourcesInstallAllowed(): Boolean {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val canRequest = packageManager.canRequestPackageInstalls()
            if (!canRequest) {
                val uri = Uri.parse("package:$packageName")
                val intent = Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, uri)
                startActivity(intent)
                return false
            }
        }
        return true
    }

    private suspend fun downloadApkToCache(apkUrl: String, versionCode: Int): File = withContext(Dispatchers.IO) {
        val client = OkHttpClient.Builder()
            .connectTimeout(30, TimeUnit.SECONDS)
            .readTimeout(180, TimeUnit.SECONDS)
            .writeTimeout(180, TimeUnit.SECONDS)
            .callTimeout(240, TimeUnit.SECONDS)
            .retryOnConnectionFailure(true)
            .build()
        val request = Request.Builder().url(apkUrl).build()

        val cacheFile = File(cacheDir, "app-latest-update-$versionCode.apk")
        if (cacheFile.exists() && cacheFile.length() > 1024 * 1024) {
            runCatching {
                val magic = ByteArray(2)
                cacheFile.inputStream().use { ins ->
                    if (ins.read(magic) == 2 &&
                        magic[0] == 'P'.code.toByte() &&
                        magic[1] == 'K'.code.toByte()
                    ) {
                        return@withContext cacheFile
                    }
                }
            }
        }

        var lastError: Exception? = null
        repeat(2) { attempt ->
            try {
                client.newCall(request).execute().use { resp ->
                    if (!resp.isSuccessful) {
                        throw IllegalStateException("HTTP ${resp.code}")
                    }
                    val body = resp.body ?: throw IllegalStateException("Empty response body")
                    body.byteStream().use { input ->
                        cacheFile.outputStream().use { output ->
                            input.copyTo(output)
                        }
                    }
                }
                lastError = null
                return@repeat
            } catch (e: Exception) {
                lastError = e
                if (attempt == 0) {
                    runCatching { cacheFile.delete() }
                }
            }
        }
        lastError?.let { throw it }

        val magic = ByteArray(2)
        cacheFile.inputStream().use { ins ->
            if (ins.read(magic) != 2) {
                throw IllegalStateException("Пустой файл обновления")
            }
        }
        if (magic[0] != 'P'.code.toByte() || magic[1] != 'K'.code.toByte()) {
            throw IllegalStateException("Сервер вернул не APK-файл. Проверьте app-latest.bin")
        }

        cacheFile
    }

    private fun installApkFile(apkFile: File) {
        val uri = FileProvider.getUriForFile(
            this,
            "${BuildConfig.APPLICATION_ID}.fileprovider",
            apkFile
        )

        val intent = Intent(Intent.ACTION_INSTALL_PACKAGE).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            putExtra(Intent.EXTRA_NOT_UNKNOWN_SOURCE, true)
            putExtra(Intent.EXTRA_RETURN_RESULT, true)
        }
        try {
            startActivity(intent)
        } catch (_: ActivityNotFoundException) {
            val fallback = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(uri, "application/vnd.android.package-archive")
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            startActivity(fallback)
        }
    }
}


