package com.example.app

import android.content.Intent
import android.content.ActivityNotFoundException
import android.net.Uri
import android.os.Bundle
import android.os.Build
import android.util.Log
import androidx.appcompat.app.AlertDialog
import android.widget.ImageView
import android.widget.ProgressBar
import android.widget.TextView
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.FileProvider
import android.provider.Settings
import coil.load
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.asRequestBody
import com.example.app.api.ApiClient
import com.example.app.api.EmployeeProfile
import java.io.File
import java.util.Locale
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class ProfileActivity : BaseActivity() {
    private val authPrefs by lazy { getSharedPreferences("auth", MODE_PRIVATE) }
    private val logTag = "ProfileActivityNet"

    private lateinit var tvName: TextView
    private lateinit var tvEmployeeId: TextView
    private lateinit var tvPhone: TextView
    private lateinit var tvPosition: TextView
    private lateinit var tvSubdivision: TextView
    private lateinit var tvLevelBadge: TextView
    private lateinit var tvXpHint: TextView
    private lateinit var pbLevelXp: ProgressBar
    private lateinit var avatarView: ImageView


    private val pickAvatar = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri == null) return@registerForActivityResult
        val flags = android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION
        try {
            contentResolver.takePersistableUriPermission(uri, flags)
        } catch (_: Exception) {
        }
        val login = authPrefs.getString("login", null)?.trim().orEmpty()
        if (login.isEmpty()) {
            safeToast("Войдите в аккаунт, чтобы сохранить фото на сервере")
            return@registerForActivityResult
        }
        scope.launch {
            try {
                val mime = contentResolver.getType(uri) ?: "image/jpeg"
                val ext = when {
                    mime.contains("png", ignoreCase = true) -> ".png"
                    mime.contains("webp", ignoreCase = true) -> ".webp"
                    mime.contains("gif", ignoreCase = true) -> ".gif"
                    else -> ".jpg"
                }
                val temp = File(cacheDir, "avatar_upload$ext")
                contentResolver.openInputStream(uri)?.use { input ->
                    temp.outputStream().use { output -> input.copyTo(output) }
                } ?: run {
                    safeToast("Не удалось прочитать изображение")
                    return@launch
                }
                val body = temp.asRequestBody(mime.toMediaTypeOrNull())
                val part = MultipartBody.Part.createFormData("file", temp.name, body)
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.employeeApi.uploadAvatar(login, part)
                }
                runCatching { temp.delete() }
                val uploadBody = resp.body()
                if (!resp.isSuccessful || uploadBody == null || !uploadBody.success) {
                    safeToast(uploadBody?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                loadProfileFromNetwork()
            } catch (e: Exception) {
                val details = buildErrorDetails(e)
                Log.e(logTag, details, e)
                safeToast("${getString(R.string.error_network)} $details", long = true)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_profile)

        val lastNameFallback = (intent.getStringExtra("lastName") ?: authPrefs.getString("lastName", "")).orEmpty()
        val firstNameFallback = (intent.getStringExtra("firstName") ?: authPrefs.getString("firstName", "")).orEmpty()
        val phoneFallback = (intent.getStringExtra("phone") ?: authPrefs.getString("phone", "")).orEmpty()

        tvName = findViewById(R.id.tvName)
        tvEmployeeId = findViewById(R.id.tvEmployeeId)
        tvPhone = findViewById(R.id.tvPhone)
        tvPosition = findViewById(R.id.tvPosition)
        tvSubdivision = findViewById(R.id.tvSubdivision)
        tvLevelBadge = findViewById(R.id.tvLevelBadge)
        tvXpHint = findViewById(R.id.tvXpHint)
        pbLevelXp = findViewById(R.id.pbLevelXp)
        avatarView = findViewById(R.id.ivAvatar)

        getSharedPreferences("profile", MODE_PRIVATE).edit().remove("avatar_uri").apply()

        if (lastNameFallback.isNotBlank() || firstNameFallback.isNotBlank()) {
            tvName.text = "${lastNameFallback} ${firstNameFallback}".trim()
        }
        if (phoneFallback.isNotBlank()) {
            tvPhone.text = formatPhoneRuDisplay(phoneFallback)
        }

        bindLevelStrip(level = 1, experience = 0, xpToNext = 100)

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

        scope.launch { loadProfileFromNetwork() }
    }

    private suspend fun loadProfileFromNetwork() {
        val employeeId = (intent.getStringExtra("employeeId") ?: authPrefs.getString("employeeId", null))
        val login = (intent.getStringExtra("login") ?: authPrefs.getString("login", null))
        try {
            val response = withContext(Dispatchers.IO) {
                ApiClient.employeeApi.getProfile(employeeId = employeeId, login = login)
            }
            val body = response.body()

            if (!response.isSuccessful || body == null || !body.success || body.profile == null) {
                safeToast(body?.message ?: getString(R.string.error_network), long = true)
                return
            }

            applyProfile(body.profile)
        } catch (e: Exception) {
            val details = buildErrorDetails(e)
            Log.e(logTag, details, e)
            safeToast("${getString(R.string.error_network)} $details", long = true)
            showNetworkErrorDialog(details)
        }
    }

    private fun applyProfile(p: EmployeeProfile) {
        tvName.text = "${p.lastName} ${p.firstName}".trim()
        tvPhone.text = formatPhoneRuDisplay(p.phone)
        tvEmployeeId.text = p.employeeId
        tvPosition.text = p.position
        tvSubdivision.text = p.subdivision
        bindLevelStrip(level = p.level, experience = p.experience, xpToNext = p.xpToNext)
        val url = p.avatarUrl?.trim()
        if (!url.isNullOrEmpty()) {
            avatarView.load(url) {
                placeholder(R.drawable.ic_launcher_simple)
                error(R.drawable.ic_launcher_simple)
            }
        } else {
            avatarView.setImageResource(R.drawable.ic_launcher_simple)
        }
    }

    private fun bindLevelStrip(level: Int, experience: Int, xpToNext: Int) {
        tvLevelBadge.text = "Уровень $level"
        val expInLevel = experience % 100
        pbLevelXp.progress = expInLevel
        tvXpHint.text = "$expInLevel / 100 опыта · ещё $xpToNext до уровня ${level + 1}"
    }

    private fun formatPhoneRuDisplay(raw: String): String {
        val digits = raw.filter { it.isDigit() }
        val last10 = when {
            digits.length >= 11 && (digits[0] == '7' || digits[0] == '8') -> digits.takeLast(10)
            digits.length == 10 -> digits
            else -> return "+7 ••• ••• •• ••"
        }
        return "+7 ••• •••-${last10.substring(6, 8)}-${last10.substring(8, 10)}"
    }

    override fun onResume() {
        super.onResume()
        SessionManager.touch(this)
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


