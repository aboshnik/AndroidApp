package com.example.app.chats

import android.os.Bundle
import android.net.Uri
import android.content.Intent
import android.view.View
import android.widget.EditText
import android.widget.ImageButton
import android.widget.ImageView
import android.widget.ProgressBar
import android.widget.TextView
import android.content.ClipData
import android.content.ClipboardManager
import android.os.Handler
import android.os.Looper
import android.webkit.MimeTypeMap
import coil.load
import com.example.app.api.BotProfileItem
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import com.example.app.BaseActivity
import com.example.app.MediaViewerActivity
import com.example.app.MyFirebaseMessagingService
import com.example.app.R
import com.example.app.SessionManager
import com.example.app.api.ApiClient
import com.example.app.api.EditMessageRequest
import com.example.app.api.MessageItem
import com.example.app.api.SendMessageRequest
import com.example.app.api.UpdateBotProfileRequest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import coil.request.videoFrameMillis
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

class ChatActivity : BaseActivity() {
    companion object {
        const val EXTRA_THREAD_ID = "thread_id"
        const val EXTRA_THREAD_TITLE = "thread_title"
        const val EXTRA_THREAD_TYPE = "thread_type"
        const val EXTRA_THREAD_IS_TECH_ADMIN = "thread_is_tech_admin"
        const val EXTRA_THREAD_BOT_ID = "thread_bot_id"
        const val EXTRA_THREAD_IS_OFFICIAL_BOT = "thread_is_official_bot"
    }

    private var threadId: Int = 0
    private var employeeId: String = ""
    private var accountLogin: String = ""
    private var threadType: String = ""
    private var threadIsTechAdmin: Boolean = false
    private var threadBotId: String = ""
    private var threadIsOfficialBot: Boolean = false
    private var channelMuted: Boolean = false
    private var isTechAdminSelf: Boolean = false
    private var pendingBotAvatarPick: ((Uri) -> Unit)? = null
    private val pendingMediaUris = mutableListOf<Uri>()

    private lateinit var recycler: RecyclerView
    private lateinit var progress: ProgressBar
    private lateinit var empty: TextView
    private lateinit var adapter: MessagesAdapter
    private var replyTo: MessageItem? = null
    private val selectedIds = linkedSetOf<Int>()
    private var highlightedMessageId: Int? = null
    private val uiHandler = Handler(Looper.getMainLooper())
    private val refreshIntervalMs = 2200L
    private val refreshRunnable = object : Runnable {
        override fun run() {
            loadMessages(silent = true)
            uiHandler.postDelayed(this, refreshIntervalMs)
        }
    }
    private val pickBotAvatar = registerForActivityResult(androidx.activity.result.contract.ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) pendingBotAvatarPick?.invoke(uri)
        pendingBotAvatarPick = null
    }

    private val pickChatMedia = registerForActivityResult(ActivityResultContracts.PickMultipleVisualMedia(10)) { uris ->
        setPendingMedia(uris.orEmpty())
    }
    private val pickApkDocument = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) setPendingMedia(listOf(uri))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_chat)

        threadId = intent.getIntExtra(EXTRA_THREAD_ID, 0)
        MyFirebaseMessagingService.clearChatNotification(this, threadId)
        val title = intent.getStringExtra(EXTRA_THREAD_TITLE).orEmpty()
        threadType = intent.getStringExtra(EXTRA_THREAD_TYPE).orEmpty()
        threadBotId = intent.getStringExtra(EXTRA_THREAD_BOT_ID)?.trim().orEmpty()
        threadIsTechAdmin = intent.getBooleanExtra(EXTRA_THREAD_IS_TECH_ADMIN, false)
        threadIsOfficialBot = intent.getBooleanExtra(EXTRA_THREAD_IS_OFFICIAL_BOT, false)
        val titleText = buildThreadTitle(title)
        val tvTitle = findViewById<TextView>(R.id.tvChatTitle)
        tvTitle.text = titleText
        updateOfficialBadgeVisibility()
        findViewById<View>(R.id.btnChatBack).setOnClickListener { finish() }

        val auth = getSharedPreferences("auth", MODE_PRIVATE)
        employeeId = auth.getString("employeeId", "")?.trim().orEmpty()
        accountLogin = auth.getString("login", "")?.trim().orEmpty()
        isTechAdminSelf = auth.getBoolean("isTechAdmin", false)
        if (threadType.equals("bot", ignoreCase = true) && threadBotId.isNotBlank()) {
            tvTitle.setOnClickListener { openBotProfileDialog() }
        }

        recycler = findViewById(R.id.recyclerMessages)
        progress = findViewById(R.id.messagesProgress)
        empty = findViewById(R.id.messagesEmpty)

        adapter = MessagesAdapter(
            selfAliases = setOf(employeeId, accountLogin)
                .map { it.trim().lowercase() }
                .filter { it.isNotBlank() }
                .toSet(),
            onClick = { item ->
                // Telegram-like:
                // - if selection mode active -> toggle selection
                // - else -> open message menu (even when tapping on text)
                if (selectedIds.isNotEmpty()) {
                    if (selectedIds.contains(item.id)) selectedIds.remove(item.id) else selectedIds.add(item.id)
                    updateSelectionUi()
                    adapter.notifyDataSetChanged()
                } else {
                    showMessageMenu(item)
                }
            },
            onMediaClick = { item, url ->
                if (selectedIds.isNotEmpty()) {
                    if (selectedIds.contains(item.id)) selectedIds.remove(item.id) else selectedIds.add(item.id)
                    updateSelectionUi()
                    adapter.notifyDataSetChanged()
                } else {
                    val media = collectMediaUrlsForViewer()
                    val startIndex = media.indexOfFirst { it == url }.let { if (it >= 0) it else 0 }
                    startActivity(
                        Intent(this, MediaViewerActivity::class.java)
                            .putExtra(MediaViewerActivity.EXTRA_URL, url)
                            .putStringArrayListExtra(MediaViewerActivity.EXTRA_MEDIA_URLS, ArrayList(media))
                            .putExtra(MediaViewerActivity.EXTRA_START_INDEX, startIndex)
                    )
                }
            },
            onLongPress = { item -> showMessageMenu(item) },
            onActionClick = { item, action -> handleMessageAction(item, action) },
            onReplyJump = { replyToId -> jumpToMessage(replyToId) },
            isSelected = { id -> selectedIds.contains(id) }
            ,
            isHighlighted = { id -> highlightedMessageId == id }
        )
        recycler.layoutManager = LinearLayoutManager(this).apply { stackFromEnd = true }
        recycler.adapter = adapter

        findViewById<View>(R.id.btnAttach).setOnClickListener {
            val canPickApk = isTechAdminSelf &&
                threadType.equals("bot", ignoreCase = true) &&
                threadBotId.equals("StekloSecurity", ignoreCase = true)
            if (canPickApk) {
                safeShowDialog(
                    androidx.appcompat.app.AlertDialog.Builder(this)
                        .setTitle("Что прикрепить?")
                        .setItems(arrayOf("Фото/видео", "APK файл")) { _, which ->
                            if (which == 1) pickApkDocument.launch(arrayOf("application/vnd.android.package-archive", "application/octet-stream"))
                            else pickChatMedia.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageAndVideo))
                        }
                        .setNegativeButton(android.R.string.cancel, null)
                )
            } else {
                pickChatMedia.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageAndVideo))
            }
        }
        findViewById<View>(R.id.btnSend).setOnClickListener { trySendMessage() }
        findViewById<View>(R.id.btnCancelMediaDraft).setOnClickListener { setPendingMedia(emptyList()) }

        findViewById<View>(R.id.btnCancelReply).setOnClickListener { setReplyTo(null) }

        findViewById<View>(R.id.btnCancelSelection).setOnClickListener { clearSelection() }
        findViewById<View>(R.id.btnDeleteSelected).setOnClickListener {
            deleteSelectedMessages()
        }

        setupChannelUi()

        loadMessages()
    }

    override fun onResume() {
        super.onResume()
        MyFirebaseMessagingService.clearChatNotification(this, threadId)
        uiHandler.removeCallbacks(refreshRunnable)
        uiHandler.postDelayed(refreshRunnable, refreshIntervalMs)
    }

    override fun onPause() {
        super.onPause()
        uiHandler.removeCallbacks(refreshRunnable)
    }

    private fun setupChannelUi() {
        val isChannel = threadType.equals("channel", ignoreCase = true)
        val btnOn = findViewById<View>(R.id.btnChannelNotifyOn)
        val btnOff = findViewById<View>(R.id.btnChannelNotifyOff)
        btnOn.visibility = if (isChannel) View.VISIBLE else View.GONE
        btnOff.visibility = View.GONE

        btnOn.setOnClickListener {
            channelMuted = false
            safeToast("Уведомления включены")
            btnOn.visibility = View.VISIBLE
            btnOff.visibility = View.GONE
        }
        btnOff.setOnClickListener {
            channelMuted = true
            safeToast("Уведомления отключены")
            btnOn.visibility = View.GONE
            btnOff.visibility = View.VISIBLE
        }
        // default: notifications ON
        if (isChannel && channelMuted) {
            btnOn.visibility = View.GONE
            btnOff.visibility = View.VISIBLE
        }
    }

    private fun showMessageMenu(item: MessageItem) {
        val isAlreadySelected = selectedIds.contains(item.id)
        val senderId = item.senderId?.trim().orEmpty()
        val isOwnUserMessage = item.senderType.equals("user", ignoreCase = true) &&
            (senderId.equals(employeeId, ignoreCase = true) || senderId.equals(accountLogin, ignoreCase = true))
        val canDelete = isOwnUserMessage
        val canEditText = isOwnUserMessage && item.text.isNotBlank()

        val options = mutableListOf<String>()
        options += if (isAlreadySelected) "Снять выбор" else "Выбрать"
        options += "Ответить"
        if (canEditText) options += "Редактировать текст"
        options += "Копировать текст"
        options += "Выделить текст"
        if (canDelete) options += "Удалить"

        safeShowDialog(
            androidx.appcompat.app.AlertDialog.Builder(this)
                .setTitle("Сообщение")
                .setItems(options.toTypedArray()) { _, which ->
                    when (options[which]) {
                        "Выбрать" -> {
                            selectedIds.add(item.id)
                            updateSelectionUi()
                        }
                        "Снять выбор" -> {
                            selectedIds.remove(item.id)
                            updateSelectionUi()
                        }
                        "Ответить" -> setReplyTo(item)
                        "Редактировать текст" -> editMessageText(item)
                        "Копировать текст" -> copyToClipboard(item.text)
                        "Выделить текст" -> showSelectableText(item.text)
                        "Удалить" -> {
                            selectedIds.clear()
                            selectedIds.add(item.id)
                            updateSelectionUi()
                            deleteSelectedMessages()
                        }
                    }
                    adapter.notifyDataSetChanged()
                }
                .setNegativeButton(android.R.string.cancel, null)
        )
    }

    private fun editMessageText(item: MessageItem) {
        val input = EditText(this).apply {
            setText(item.text)
            setSelection(text.length)
            minLines = 2
            maxLines = 6
        }
        safeShowDialog(
            androidx.appcompat.app.AlertDialog.Builder(this)
                .setTitle("Редактировать текст")
                .setView(input)
                .setPositiveButton("Сохранить") { _, _ ->
                    val newText = input.text?.toString()?.trim().orEmpty()
                    if (newText.isBlank()) {
                        safeToast("Текст не может быть пустым", long = true)
                        return@setPositiveButton
                    }
                    if (newText == item.text.trim()) return@setPositiveButton
                    scope.launch {
                        try {
                            val resp = withContext(Dispatchers.IO) {
                                ApiClient.chatApi.editMessage(
                                    threadId = threadId,
                                    messageId = item.id,
                                    body = EditMessageRequest(login = employeeId, text = newText)
                                )
                            }
                            val body = resp.body()
                            if (!resp.isSuccessful || body == null || !body.success) {
                                safeToast(body?.message ?: getString(R.string.error_network), long = true)
                                return@launch
                            }
                            loadMessages(silent = true)
                        } catch (e: Exception) {
                            safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
                        }
                    }
                }
                .setNegativeButton("Отмена", null)
        )
    }

    private fun setReplyTo(item: MessageItem?) {
        replyTo = item
        val v = findViewById<View>(R.id.replyPreview)
        val tv = v.findViewById<TextView>(R.id.tvReplyText)
        if (item == null) {
            v.visibility = View.GONE
        } else {
            val who = buildSenderLabel(item)
            val snippet = replySnippetForMessage(item).ifBlank { "Сообщение" }
            tv.text = "Ответ $who: $snippet"
            v.visibility = View.VISIBLE
        }
    }

    private fun replySnippetForMessage(item: MessageItem): String {
        val t = item.text.trim()
        if (t.isNotEmpty()) return t.take(120)
        return runCatching {
            val o = JSONObject(item.metaJson.orEmpty())
            if (o.optJSONArray("media")?.length() ?: 0 > 0) {
                val first = o.optJSONArray("media")?.optJSONObject(0)
                val kind = first?.optString("kind", "image").orEmpty()
                return@runCatching if (kind.equals("video", ignoreCase = true)) "Видео" else "Фото"
            }
            if (o.has("mediaUrl")) {
                if (o.optString("mediaKind", "image").equals("video", ignoreCase = true)) "Видео" else "Фото"
            } else ""
        }.getOrDefault("")
    }

    private fun buildMessageMetaJson(reply: MessageItem? = null, media: List<Pair<String, String>> = emptyList()): String? {
        val o = JSONObject()
        reply?.let {
            o.put("replyToId", it.id)
            o.put("replyText", replySnippetForMessage(it).take(120))
            o.put("replySender", buildSenderLabel(it))
        }
        if (media.isNotEmpty()) {
            val first = media.first()
            o.put("mediaUrl", first.first)
            o.put("mediaKind", first.second)
            if (media.size > 1) {
                val arr = JSONArray()
                media.forEach { (url, kind) ->
                    arr.put(JSONObject().put("url", url).put("kind", kind))
                }
                o.put("media", arr)
            }
        }
        if (o.length() == 0) return null
        return o.toString()
    }

    private fun buildSenderLabel(item: MessageItem): String {
        val st = item.senderType.trim().lowercase()
        val id = item.senderId?.trim().orEmpty()
        val name = item.senderName?.trim().orEmpty()
        return when (st) {
            "bot" -> name.ifBlank { id.ifBlank { "Бот" } }
            "system" -> name.ifBlank { id.ifBlank { "Система" } }
            "user" -> {
                val base = name.ifBlank { id.ifBlank { "Пользователь" } }
                if (item.senderIsTechAdmin) "$base \uD83D\uDD27" else base
            }
            else -> name.ifBlank { id.ifBlank { "Пользователь" } }
        }
    }

    private fun handleMessageAction(item: MessageItem, action: String) {
        if (action.startsWith("open_apk:", ignoreCase = true)) {
            val apkUrl = action.substringAfter("open_apk:", "").trim()
            if (apkUrl.isBlank()) {
                safeToast("Ссылка на APK не найдена", long = true)
                return
            }
            safeShowDialog(
                androidx.appcompat.app.AlertDialog.Builder(this)
                    .setTitle("Скачать APK")
                    .setMessage("Открыть ссылку для скачивания APK?")
                    .setPositiveButton("Скачать APK") { _, _ ->
                        val opened = runCatching {
                            val i = Intent(Intent.ACTION_VIEW, Uri.parse(apkUrl))
                            startActivity(i)
                            true
                        }.getOrDefault(false)
                        if (!opened) safeToast("Не удалось открыть ссылку APK", long = true)
                    }
                    .setNegativeButton("Отмена", null)
            )
            return
        }
        if (!item.senderType.equals("bot", ignoreCase = true)) return
        val sender = item.senderId?.trim().orEmpty()
        if (!sender.equals("StekloSecurity", ignoreCase = true)) return
        if (!action.equals("relogin_account", ignoreCase = true)) return
        val meta = item.metaJson?.trim().orEmpty()
        if (meta.isBlank()) return
        val login = runCatching { JSONObject(meta).optString("actionLogin", "").trim() }.getOrDefault("")
        val password = runCatching { JSONObject(meta).optString("actionPassword", "").trim() }.getOrDefault("")
        val autoSeconds = runCatching { JSONObject(meta).optInt("actionAutoSeconds", 0) }.getOrDefault(0)
        if (login.isBlank() || password.isBlank()) {
            safeToast("Данные для перезахода не найдены", long = true)
            return
        }
        SessionManager.clear(this)
        val i = Intent(this, com.example.app.MainActivity::class.java).apply {
            putExtra(com.example.app.MainActivity.EXTRA_PREFILL_LOGIN, login)
            putExtra(com.example.app.MainActivity.EXTRA_PREFILL_PASSWORD, password)
            putExtra(com.example.app.MainActivity.EXTRA_PREFILL_REMEMBER_ME, true)
            putExtra(com.example.app.MainActivity.EXTRA_AUTO_LOGIN_SECONDS, autoSeconds.coerceAtLeast(0))
            putExtra(com.example.app.MainActivity.EXTRA_RELOGIN_BYPASS_DEVICE_CODE, true)
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        }
        startActivity(i)
        finishAffinity()
    }

    private fun copyToClipboard(text: String) {
        val cm = getSystemService(CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText("message", text))
        safeToast("Скопировано")
    }

    private fun showSelectableText(text: String) {
        val tv = TextView(this).apply {
            setTextIsSelectable(true)
            setPadding(32, 24, 32, 24)
            this.text = text
            setTextColor(getColor(R.color.text_primary))
        }
        safeShowDialog(
            androidx.appcompat.app.AlertDialog.Builder(this)
                .setTitle("Текст сообщения")
                .setView(tv)
                .setPositiveButton("OK", null)
        )
    }

    private fun updateSelectionUi() {
        val bar = findViewById<View>(R.id.selectionBar)
        val tv = findViewById<TextView>(R.id.tvSelectedCount)
        if (selectedIds.isEmpty()) {
            bar.visibility = View.GONE
            return
        }
        tv.text = "${selectedIds.size} выбрано"
        bar.visibility = View.VISIBLE
    }

    private fun clearSelection() {
        selectedIds.clear()
        updateSelectionUi()
        adapter.notifyDataSetChanged()
    }

    private fun jumpToMessage(messageId: Int) {
        fun highlightIfPresent(): Boolean {
            val idx = adapter.currentList.indexOfFirst { it.id == messageId }
            if (idx < 0) return false
            recycler.smoothScrollToPosition(idx)
            highlightedMessageId = messageId
            adapter.notifyDataSetChanged()
            uiHandler.postDelayed({
                if (highlightedMessageId == messageId) {
                    highlightedMessageId = null
                    adapter.notifyDataSetChanged()
                }
            }, 2200)
            return true
        }

        if (highlightIfPresent()) return
        loadMessages(silent = true)
        uiHandler.postDelayed({
            if (!highlightIfPresent()) {
                safeToast("Сообщение не найдено", long = true)
            }
        }, 420)
    }

    private fun deleteSelectedMessages() {
        if (selectedIds.isEmpty()) return
        val ids = selectedIds.toList()
        scope.launch {
            try {
                var anyFailed = false
                withContext(Dispatchers.IO) {
                    for (id in ids) {
                        val r = ApiClient.chatApi.deleteMessage(threadId = threadId, messageId = id, login = employeeId)
                        val b = r.body()
                        if (!r.isSuccessful || b == null || !b.success) anyFailed = true
                    }
                }
                clearSelection()
                loadMessages(silent = true)
                if (anyFailed) safeToast("Часть сообщений не удалось удалить", long = true)
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun uploadAndSendChatMedia(uris: List<Uri>) {
        if (threadId <= 0 || employeeId.isBlank()) return
        if (uris.isEmpty()) return
        val btnSend = findViewById<ImageButton>(R.id.btnSend)
        val btnAttach = findViewById<ImageButton>(R.id.btnAttach)
        btnSend.isEnabled = false
        btnAttach.isEnabled = false
        scope.launch {
            val tempFiles = mutableListOf<File>()
            try {
                val uploaded = mutableListOf<Pair<String, String>>()
                for (uri in uris) {
                    val mime = contentResolver.getType(uri) ?: "application/octet-stream"
                    val ext = when {
                        mime.contains("png", true) -> ".png"
                        mime.contains("webp", true) -> ".webp"
                        mime.contains("gif", true) -> ".gif"
                        mime.contains("webm", true) -> ".webm"
                        mime.contains("video", true) || mime.contains("mp4", true) || mime.contains("quicktime", true) -> ".mp4"
                        mime.contains("android.package-archive", true) || mime.contains("apk", true) || isApkUri(uri) -> ".apk"
                        else -> ".jpg"
                    }
                    val f = File(cacheDir, "chat_upload_${System.currentTimeMillis()}_${uploaded.size}$ext")
                    tempFiles += f
                    contentResolver.openInputStream(uri)?.use { input ->
                        f.outputStream().use { output -> input.copyTo(output) }
                    } ?: run {
                        safeToast("Не удалось прочитать файл", long = true)
                        return@launch
                    }
                    val part = MultipartBody.Part.createFormData(
                        "file",
                        f.name,
                        f.asRequestBody(mime.toMediaTypeOrNull())
                    )
                    val up = withContext(Dispatchers.IO) {
                        ApiClient.chatApi.uploadChatMedia(threadId = threadId, login = employeeId, file = part)
                    }
                    val upBody = up.body()
                    if (!up.isSuccessful || upBody == null || !upBody.success || upBody.url.isNullOrBlank()) {
                        safeToast(upBody?.message ?: getString(R.string.error_network), long = true)
                        return@launch
                    }
                    val kind = when {
                        upBody.kind.equals("video", ignoreCase = true) -> "video"
                        upBody.kind.equals("apk", ignoreCase = true) -> "apk"
                        else -> "image"
                    }
                    uploaded += upBody.url to kind
                }
                val et = findViewById<EditText>(R.id.etMessage)
                val caption = et.text?.toString()?.trim().orEmpty()
                val meta = buildMessageMetaJson(reply = replyTo, media = uploaded)
                val send = withContext(Dispatchers.IO) {
                    ApiClient.chatApi.sendMessage(
                        threadId = threadId,
                        body = SendMessageRequest(login = employeeId, text = caption, metaJson = meta)
                    )
                }
                val sendBody = send.body()
                if (!send.isSuccessful || sendBody == null || !sendBody.success || sendBody.item == null) {
                    safeToast(sendBody?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                et.setText("")
                setReplyTo(null)
                setPendingMedia(emptyList())
                loadMessages(silent = true)
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            } finally {
                tempFiles.forEach { runCatching { it.delete() } }
                btnSend.isEnabled = true
                btnAttach.isEnabled = true
            }
        }
    }

    private fun trySendMessage() {
        if (pendingMediaUris.isNotEmpty()) {
            uploadAndSendChatMedia(pendingMediaUris.toList())
            return
        }
        val et = findViewById<EditText>(R.id.etMessage)
        val text = et.text?.toString()?.trim().orEmpty()
        if (text.isBlank()) return

        val btnSend = findViewById<ImageButton>(R.id.btnSend)
        val btnAttach = findViewById<ImageButton>(R.id.btnAttach)
        btnSend.isEnabled = false
        btnAttach.isEnabled = false

        scope.launch {
            try {
                val meta = buildMessageMetaJson(reply = replyTo, media = emptyList())
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.chatApi.sendMessage(
                        threadId = threadId,
                        body = SendMessageRequest(login = employeeId, text = text, metaJson = meta)
                    )
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success || body.item == null) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                et.setText("")
                setReplyTo(null)
                loadMessages(silent = true)
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            } finally {
                btnSend.isEnabled = true
                btnAttach.isEnabled = true
            }
        }
    }

    private fun loadMessages(silent: Boolean = false) {
        if (employeeId.isBlank() || threadId <= 0) {
            empty.text = getString(R.string.error_network)
            empty.visibility = View.VISIBLE
            return
        }

        if (!silent) {
            progress.visibility = View.VISIBLE
            empty.visibility = View.GONE
        }
        val lm = recycler.layoutManager as? LinearLayoutManager
        val oldSize = adapter.currentList.size
        val oldLastVisible = lm?.findLastVisibleItemPosition() ?: -1
        val wasAtBottom = oldSize == 0 || oldLastVisible >= oldSize - 2

        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.chatApi.getMessages(threadId = threadId, login = employeeId, take = 120)
                }
                if (!silent) progress.visibility = View.GONE
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    empty.text = body?.message ?: getString(R.string.error_network)
                    empty.visibility = View.VISIBLE
                    if (!silent) adapter.submit(emptyList())
                    return@launch
                }
                val list: List<MessageItem> = body.messages.orEmpty()
                adapter.submit(list)
                empty.visibility = if (list.isEmpty()) View.VISIBLE else View.GONE
                if (list.isNotEmpty() && (wasAtBottom || list.size > oldSize)) {
                    recycler.scrollToPosition(list.size - 1)
                }
            } catch (e: Exception) {
                if (!silent) {
                    progress.visibility = View.GONE
                    empty.text = "${getString(R.string.error_network)} ${e.message}"
                    empty.visibility = View.VISIBLE
                    adapter.submit(emptyList())
                }
            }
        }
    }

    private fun openBotProfileDialog() {
        if (threadBotId.isBlank()) return
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) { ApiClient.chatApi.getBotProfile(threadBotId, employeeId) }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success || body.profile == null) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                threadIsOfficialBot = body.profile.isOfficial
                findViewById<TextView>(R.id.tvChatTitle).text = buildThreadTitle(body.profile.displayName)
                updateOfficialBadgeVisibility()
                showBotProfileDialog(body.profile)
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun showBotProfileDialog(profile: BotProfileItem) {
        val view = layoutInflater.inflate(R.layout.dialog_bot_profile, null, false)
        val ivAvatar = view.findViewById<ImageView>(R.id.ivBotAvatar)
        val tvName = view.findViewById<TextView>(R.id.tvBotName)
        val etDescription = view.findViewById<EditText>(R.id.etBotDescription)
        val swOfficial = view.findViewById<android.widget.Switch>(R.id.swOfficialBot)
        val btnPhoto = view.findViewById<View>(R.id.btnChangeBotPhoto)
        val btnSave = view.findViewById<View>(R.id.btnSaveBotProfile)

        tvName.text = profile.displayName + if (profile.isOfficial) " ✓" else ""
        etDescription.setText(profile.description.orEmpty())
        swOfficial.isChecked = profile.isOfficial
        ivAvatar.load(profile.avatarUrl) { error(R.drawable.ic_launcher_simple) }

        val canEdit = isTechAdminSelf
        etDescription.isEnabled = canEdit
        swOfficial.isEnabled = canEdit
        btnPhoto.visibility = if (canEdit) View.VISIBLE else View.GONE
        btnSave.visibility = if (canEdit) View.VISIBLE else View.GONE

        val dialog = safeShowDialog(
            androidx.appcompat.app.AlertDialog.Builder(this)
                .setView(view)
                .setNegativeButton(android.R.string.cancel, null)
        ) ?: return

        btnPhoto.setOnClickListener {
            pendingBotAvatarPick = { uri -> uploadBotAvatar(uri) { openBotProfileDialog() } }
            pickBotAvatar.launch(arrayOf("image/*"))
        }
        btnSave.setOnClickListener {
            val desc = etDescription.text?.toString()?.trim().orEmpty()
            updateBotProfile(desc, swOfficial.isChecked) {
                dialog.dismiss()
                openBotProfileDialog()
            }
        }
    }

    private fun updateBotProfile(description: String, isOfficial: Boolean, onDone: () -> Unit) {
        if (threadBotId.isBlank()) return
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.chatApi.updateBotProfile(
                        botId = threadBotId,
                        body = UpdateBotProfileRequest(
                            login = employeeId,
                            description = description,
                            isOfficial = isOfficial
                        )
                    )
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                threadIsOfficialBot = body.profile?.isOfficial == true
                val title = body.profile?.displayName?.trim().orEmpty().ifBlank {
                    findViewById<TextView>(R.id.tvChatTitle).text.toString()
                }
                findViewById<TextView>(R.id.tvChatTitle).text = buildThreadTitle(title)
                updateOfficialBadgeVisibility()
                safeToast("Профиль бота обновлен")
                onDone()
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun uploadBotAvatar(uri: Uri, onDone: () -> Unit) {
        if (threadBotId.isBlank()) return
        scope.launch {
            try {
                val mime = contentResolver.getType(uri) ?: "image/jpeg"
                val ext = when {
                    mime.contains("png", true) -> ".png"
                    mime.contains("webp", true) -> ".webp"
                    mime.contains("gif", true) -> ".gif"
                    else -> ".jpg"
                }
                val temp = File(cacheDir, "bot_avatar$ext")
                contentResolver.openInputStream(uri)?.use { input ->
                    temp.outputStream().use { output -> input.copyTo(output) }
                } ?: run {
                    safeToast("Не удалось прочитать файл", long = true)
                    return@launch
                }
                val part = MultipartBody.Part.createFormData(
                    "file",
                    temp.name,
                    temp.asRequestBody(mime.toMediaTypeOrNull())
                )
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.chatApi.uploadBotAvatar(threadBotId, employeeId, part)
                }
                runCatching { temp.delete() }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                safeToast("Фото бота обновлено")
                onDone()
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun buildThreadTitle(base: String): String {
        val name = base.ifBlank { "Чат" }
        val tech = threadIsTechAdmin
        val suffix = buildString {
            if (tech) append(" \uD83D\uDD27")
        }
        return name + suffix
    }

    private fun updateOfficialBadgeVisibility() {
        val show = threadType.equals("bot", ignoreCase = true) && threadIsOfficialBot
        findViewById<View>(R.id.ivChatOfficialBadge).visibility = if (show) View.VISIBLE else View.GONE
    }

    private fun collectMediaUrlsForViewer(): List<String> {
        val out = linkedSetOf<String>()
        for (m in adapter.currentList) {
            extractMediaUrls(m.metaJson).forEach { if (it.isNotBlank()) out += it }
        }
        return out.toList()
    }

    private fun extractMediaUrls(metaJson: String?): List<String> {
        val meta = metaJson?.trim().orEmpty()
        if (meta.isBlank()) return emptyList()
        return runCatching {
            val o = JSONObject(meta)
            val out = linkedSetOf<String>()
            val arr = o.optJSONArray("media")
            if (arr != null) {
                for (i in 0 until arr.length()) {
                    val e = arr.optJSONObject(i) ?: continue
                    val kind = e.optString("kind", "image").trim().lowercase()
                    if (kind != "image" && kind != "video") continue
                    val u = e.optString("url", "").trim()
                    if (u.isNotBlank()) out += u
                }
            }
            val one = o.optString("mediaUrl", "").trim()
            val oneKind = o.optString("mediaKind", "image").trim().lowercase()
            if (one.isNotBlank() && (oneKind == "image" || oneKind == "video")) out += one
            out.toList()
        }.getOrDefault(emptyList())
    }

    private fun setPendingMedia(uris: List<Uri>) {
        pendingMediaUris.clear()
        pendingMediaUris.addAll(uris)
        val preview = findViewById<View>(R.id.mediaDraftPreview)
        val thumb = findViewById<ImageView>(R.id.ivMediaDraftThumb)
        val label = findViewById<TextView>(R.id.tvMediaDraftLabel)
        if (pendingMediaUris.isEmpty()) {
            preview.visibility = View.GONE
            thumb.setImageDrawable(null)
            label.text = ""
            return
        }
        val uri = pendingMediaUris.first()
        val video = isVideoUri(uri)
        val apks = pendingMediaUris.count { isApkUri(it) }
        val videos = pendingMediaUris.count { isVideoUri(it) }
        val photos = pendingMediaUris.size - videos - apks
        label.text = buildString {
            if (apks > 0) append("APK: $apks")
            if (apks > 0 && (photos > 0 || videos > 0)) append("  ")
            if (photos > 0) append("Фото: $photos")
            if (photos > 0 && videos > 0) append("  ")
            if (videos > 0) append("Видео: $videos")
            append(" — можно добавить подпись")
        }
        if (isApkUri(uri)) {
            thumb.setImageResource(android.R.drawable.sym_def_app_icon)
        } else {
            thumb.load(uri) {
                crossfade(true)
                if (video) videoFrameMillis(800L)
                error(R.drawable.ic_launcher_simple)
                placeholder(R.drawable.ic_launcher_simple)
            }
        }
        preview.visibility = View.VISIBLE
    }

    private fun isVideoUri(uri: Uri): Boolean {
        val mime = contentResolver.getType(uri)?.trim()?.lowercase().orEmpty()
        if (mime.startsWith("video/")) return true
        val ext = MimeTypeMap.getFileExtensionFromUrl(uri.toString())?.trim()?.lowercase().orEmpty()
        return ext in setOf("mp4", "mov", "mkv", "webm", "avi", "m4v", "3gp")
    }

    private fun isApkUri(uri: Uri): Boolean {
        val mime = contentResolver.getType(uri)?.trim()?.lowercase().orEmpty()
        if (mime.contains("android.package-archive")) return true
        val ext = MimeTypeMap.getFileExtensionFromUrl(uri.toString())?.trim()?.lowercase().orEmpty()
        return ext == "apk"
    }
}

