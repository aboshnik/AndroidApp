package com.example.app

import android.content.Intent
import android.os.Bundle
import android.util.Log
import androidx.appcompat.app.AlertDialog
import android.widget.ImageView
import android.widget.ProgressBar
import android.widget.TextView
import androidx.activity.result.contract.ActivityResultContracts
import coil.load
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import com.example.app.api.ApiClient
import com.example.app.api.EmployeeProfile
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import com.example.app.chats.ChatsActivity

class ProfileActivity : BaseActivity() {
    override fun swipeTabIndex(): Int = 3
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
    private lateinit var techAdminBadge: ImageView
    private var canUseDevConsole: Boolean = false


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
        techAdminBadge = findViewById(R.id.ivTechAdminBadge)

        getSharedPreferences("profile", MODE_PRIVATE).edit().remove("avatar_uri").apply()

        if (lastNameFallback.isNotBlank() || firstNameFallback.isNotBlank()) {
            tvName.text = "${lastNameFallback} ${firstNameFallback}".trim()
        }
        val isTechAdmin = authPrefs.getBoolean("isTechAdmin", false)
        techAdminBadge.visibility = if (isTechAdmin) android.view.View.VISIBLE else android.view.View.GONE
        if (phoneFallback.isNotBlank()) {
            tvPhone.text = formatPhoneRuDisplay(phoneFallback)
        }

        bindLevelStrip(level = 1, experience = 0, xpToNext = 100)

        avatarView.setOnClickListener {
            pickAvatar.launch(arrayOf("image/*"))
        }
        findViewById<android.view.View>(R.id.btnAvatarEdit).setOnClickListener {
            pickAvatar.launch(arrayOf("image/*"))
        }

        val rowLogout = findViewById<android.view.View>(R.id.rowLogout)
        val rowCalendar = findViewById<android.view.View>(R.id.rowCalendar)

        listOf(rowCalendar, rowLogout).forEach {
            attachPressAnimation(it)
        }

        rowCalendar.setOnClickListener {
            startActivity(Intent(this, CalendarActivity::class.java))
        }
        rowLogout.setOnClickListener {
            confirmLogout()
        }

        findViewById<android.view.View>(R.id.navHome).setOnClickListener {
            startActivity(Intent(this, HomeActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navChats).setOnClickListener {
            startActivity(Intent(this, ChatsActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navSettings).setOnClickListener {
            startActivity(Intent(this, SettingsActivity::class.java))
        }
        findViewById<android.view.View>(R.id.navProfile).setOnClickListener { }
        findViewById<android.view.View>(R.id.navContacts).setOnClickListener {
            startActivity(Intent(this, ShopActivity::class.java))
        }
        setBottomTab("profile")

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
        val balance = if (p.coinBalance > 0) p.coinBalance else p.experience
        val nextPayoutDays = if (p.nextPayoutDays >= 0) p.nextPayoutDays else p.xpToNext
        bindLevelStrip(level = p.level, experience = balance, xpToNext = nextPayoutDays)
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
        val balance = experience.coerceAtLeast(0)
        val nextPayoutDays = xpToNext.coerceAtLeast(0)
        tvLevelBadge.text = "Ваш баланс $balance"
        pbLevelXp.max = 100
        pbLevelXp.progress = (100 - nextPayoutDays.coerceAtMost(100))
        tvXpHint.text = "Следующая выдача 5 монет будет через $nextPayoutDays дней"
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
        setBottomTab("profile")
        SessionManager.touch(this)
        val employeeId = authPrefs.getString("employeeId", "")?.trim().orEmpty()
        startChatsUnreadBadgeAutoRefresh(employeeId = employeeId, badgeViewId = R.id.navChatsBadge)
    }

    override fun onPause() {
        super.onPause()
        stopChatsUnreadBadgeAutoRefresh()
    }

    // applyForceUpdateUiState removed

    override fun onSupportNavigateUp(): Boolean {
        finish()
        return true
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

    private fun showContactsDialog() {
        val view = layoutInflater.inflate(R.layout.dialog_contacts, null, false)
        safeShowDialog(
            AlertDialog.Builder(this)
                .setView(view)
                .setPositiveButton("Закрыть", null)
        )
    }

    private fun setBottomTab(tab: String) {
        val active = getColor(R.color.nav_active)
        val inactive = getColor(R.color.nav_inactive)
        fun setItem(containerId: Int, iconId: Int, textId: Int, activeTab: Boolean) {
            val container = findViewById<android.view.View>(containerId)
            findViewById<ImageView>(iconId).setColorFilter(if (activeTab) active else inactive)
            val tv = findViewById<TextView>(textId)
            tv.setTextColor(if (activeTab) active else inactive)
            tv.setTypeface(null, if (activeTab) android.graphics.Typeface.BOLD else android.graphics.Typeface.NORMAL)
            container.setBackgroundResource(
                if (activeTab) R.drawable.bg_bottom_nav_item_active
                else android.R.color.transparent
            )
        }
        setItem(R.id.navHome, R.id.navHomeIcon, R.id.navHomeText, tab == "home")
        setItem(R.id.navChats, R.id.navChatsIcon, R.id.navChatsText, tab == "chats")
        setItem(R.id.navSettings, R.id.navSettingsIcon, R.id.navSettingsText, tab == "settings")
        setItem(R.id.navProfile, R.id.navProfileIcon, R.id.navProfileText, tab == "profile")
        setItem(R.id.navContacts, R.id.navContactsIcon, R.id.navContactsText, tab == "store")
    }

    private fun attachPressAnimation(v: android.view.View) {
        v.setOnTouchListener { view, event ->
            when (event.actionMasked) {
                android.view.MotionEvent.ACTION_DOWN -> {
                    view.animate().scaleX(0.98f).scaleY(0.98f).setDuration(90).start()
                }
                android.view.MotionEvent.ACTION_UP,
                android.view.MotionEvent.ACTION_CANCEL -> {
                    view.animate().scaleX(1f).scaleY(1f).setDuration(120).start()
                }
            }
            false
        }
    }

}


