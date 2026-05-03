package com.example.app

import android.net.Uri
import android.os.Bundle
import android.text.Editable
import android.text.TextWatcher
import android.view.LayoutInflater
import android.view.View
import android.view.animation.AnimationUtils
import android.widget.EditText
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.Switch
import android.widget.TextView
import androidx.appcompat.app.AlertDialog
import androidx.activity.result.contract.ActivityResultContracts
import coil.load
import com.example.app.api.ApiClient
import com.example.app.api.CreatePostRequest
import com.example.app.api.PollCreateRequest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.toRequestBody

class CreatePostActivity : BaseActivity() {
    private companion object {
        const val MAX_POST_TEXT_LENGTH = 3333
        const val MAX_POST_IMAGES = 15
        const val MAX_POST_VIDEOS = 5
    }

    private val attachments = mutableListOf<Uri>()
    private var pollDraft: PollCreateRequest? = null

    private lateinit var etPostText: EditText
    private lateinit var mediaContainer: View
    private lateinit var mediaTop: ImageView
    private lateinit var mediaBottomRow: LinearLayout
    private lateinit var mediaBottomLeft: ImageView
    private lateinit var mediaBottomRight: ImageView
    private lateinit var mediaMore: TextView
    private lateinit var pollChip: TextView
    private lateinit var postTextCounter: TextView
    private lateinit var postPreviewContainer: View
    private lateinit var postPreviewText: TextView
    private lateinit var cbEventCoins: Switch
    private lateinit var createPostSheet: View

    private val pickMedia = registerForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        if (uris.isNullOrEmpty()) return@registerForActivityResult
        val picked = uris.toList()
        val imageCount = picked.count { !isVideoUri(it) }
        val videoCount = picked.count { isVideoUri(it) }
        if (imageCount > MAX_POST_IMAGES) {
            safeToast("Максимум $MAX_POST_IMAGES фото в одной новости", long = true)
            return@registerForActivityResult
        }
        if (videoCount > MAX_POST_VIDEOS) {
            safeToast("Максимум $MAX_POST_VIDEOS видео в одной новости", long = true)
            return@registerForActivityResult
        }
        attachments.clear()
        attachments.addAll(picked)
        bindMediaPreview()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_create_post)

        etPostText = findViewById(R.id.etPostText)
        createPostSheet = findViewById(R.id.createPostSheet)
        mediaContainer = findViewById(R.id.postMediaContainer)
        mediaTop = findViewById(R.id.postMediaTop)
        mediaBottomRow = findViewById(R.id.postMediaBottomRow)
        mediaBottomLeft = findViewById(R.id.postMediaBottomLeft)
        mediaBottomRight = findViewById(R.id.postMediaBottomRight)
        mediaMore = findViewById(R.id.postMediaMoreOverlay)
        pollChip = findViewById(R.id.tvPollChip)
        postTextCounter = findViewById(R.id.tvPostTextCounter)
        postPreviewContainer = findViewById(R.id.postPreviewContainer)
        postPreviewText = findViewById(R.id.tvPostPreviewText)
        cbEventCoins = findViewById(R.id.cbEventCoins)

        findViewById<View>(R.id.btnCreatePostBack).setOnClickListener { finishWithSheetAnimation() }
        findViewById<View>(R.id.btnAttachMedia).setOnClickListener { pickMedia.launch(arrayOf("image/*", "video/*")) }
        findViewById<View>(R.id.btnAttachPoll).setOnClickListener { showCreatePollDialog() }
        findViewById<View>(R.id.btnPublishPost).setOnClickListener { publishPost() }
        findViewById<View>(R.id.btnClearPoll).setOnClickListener {
            pollDraft = null
            bindPollChip()
        }
        cbEventCoins.setOnCheckedChangeListener { _, checked ->
            if (!checked) {
            }
        }

        bindMediaPreview()
        bindPollChip()
        updatePostTextCounter()
        updatePostPreview()
        createPostSheet.startAnimation(AnimationUtils.loadAnimation(this, R.anim.sheet_slide_in_up))
        etPostText.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                updatePostTextCounter()
                updatePostPreview()
            }
            override fun afterTextChanged(s: Editable?) {}
        })
    }

    private fun bindPollChip() {
        pollChip.visibility = if (pollDraft == null) View.GONE else View.VISIBLE
        pollChip.text = if (pollDraft == null) "" else {
            "Опрос: ${pollDraft?.question.orEmpty()} • ${pollDraft?.options?.size ?: 0} вариантов"
        }
        findViewById<View>(R.id.btnClearPoll).visibility = if (pollDraft == null) View.GONE else View.VISIBLE
    }

    private fun showCreatePollDialog() {
        val dialogView = LayoutInflater.from(this).inflate(R.layout.dialog_create_poll, null, false)
        val etQuestion = dialogView.findViewById<EditText>(R.id.etPollQuestion)
        val etDescription = dialogView.findViewById<EditText>(R.id.etPollDescription)
        val optionsContainer = dialogView.findViewById<LinearLayout>(R.id.pollOptionsContainer)
        val btnAddOption = dialogView.findViewById<View>(R.id.btnAddPollOption)
        val swShowVoters = dialogView.findViewById<Switch>(R.id.swPollShowVoters)
        val swAllowRevote = dialogView.findViewById<Switch>(R.id.swPollAllowRevote)
        val swShuffle = dialogView.findViewById<Switch>(R.id.swPollShuffle)
        val swHideResults = dialogView.findViewById<Switch>(R.id.swPollHideResults)
        val btnCreate = dialogView.findViewById<View>(R.id.btnCreatePoll)
        val btnClose = dialogView.findViewById<View>(R.id.btnClosePoll)

        val existing = pollDraft
        etQuestion.setText(existing?.question.orEmpty())
        etDescription.setText(existing?.description.orEmpty())
        swShowVoters.isChecked = existing?.showVoters ?: true
        swAllowRevote.isChecked = existing?.allowRevote ?: false
        swShuffle.isChecked = existing?.shuffleOptions ?: true
        swHideResults.isChecked = existing?.hideResultsUntilEnd ?: false

        val optionInputs = mutableListOf<EditText>()
        fun addOptionRow(text: String = "") {
            val row = LayoutInflater.from(this).inflate(R.layout.item_poll_option_edit, optionsContainer, false)
            val input = row.findViewById<EditText>(R.id.etPollOption)
            val btnDelete = row.findViewById<View>(R.id.btnDeletePollOption)
            input.setText(text)
            optionInputs.add(input)
            btnDelete.setOnClickListener {
                if (optionInputs.size <= 2) {
                    safeToast("Минимум 2 варианта")
                    return@setOnClickListener
                }
                optionInputs.remove(input)
                optionsContainer.removeView(row)
            }
            optionsContainer.addView(row)
        }

        val initialOptions = existing?.options?.takeIf { it.isNotEmpty() } ?: listOf("", "")
        initialOptions.forEach { addOptionRow(it) }
        if (optionInputs.size < 2) {
            repeat(2 - optionInputs.size) { addOptionRow("") }
        }
        btnAddOption.setOnClickListener { addOptionRow("") }

        val dialog = AlertDialog.Builder(this).setView(dialogView).create()
        dialog.window?.attributes = dialog.window?.attributes?.apply {
            windowAnimations = R.style.AppPollDialogAnimation
        }
        btnClose.setOnClickListener { dialog.dismiss() }
        btnCreate.setOnClickListener {
            val question = etQuestion.text?.toString()?.trim().orEmpty()
            val options = optionInputs.map { it.text?.toString()?.trim().orEmpty() }.filter { it.isNotBlank() }
            if (question.isBlank()) {
                safeToast("Введите вопрос")
                return@setOnClickListener
            }
            if (options.size < 2) {
                safeToast("Добавьте минимум 2 варианта")
                return@setOnClickListener
            }
            pollDraft = PollCreateRequest(
                question = question,
                description = etDescription.text?.toString()?.trim().orEmpty().ifBlank { null },
                options = options,
                showVoters = swShowVoters.isChecked,
                allowRevote = swAllowRevote.isChecked,
                shuffleOptions = swShuffle.isChecked,
                hideResultsUntilEnd = swHideResults.isChecked
            )
            bindPollChip()
            dialog.dismiss()
        }
        dialog.show()
    }

    private fun finishWithSheetAnimation() {
        val out = AnimationUtils.loadAnimation(this, R.anim.sheet_slide_out_down)
        out.setAnimationListener(object : android.view.animation.Animation.AnimationListener {
            override fun onAnimationStart(animation: android.view.animation.Animation?) {}
            override fun onAnimationRepeat(animation: android.view.animation.Animation?) {}
            override fun onAnimationEnd(animation: android.view.animation.Animation?) {
                finish()
                overridePendingTransition(R.anim.fade_in_fast, R.anim.fade_out_fast)
            }
        })
        createPostSheet.startAnimation(out)
    }

    override fun onBackPressed() {
        finishWithSheetAnimation()
    }

    private fun bindMediaPreview() {
        if (attachments.isEmpty()) {
            mediaContainer.visibility = View.GONE
            return
        }
        mediaContainer.visibility = View.VISIBLE
        val list = attachments.toList()
        mediaTop.load(list[0]) { crossfade(true) }
        if (list.size == 1) {
            mediaBottomRow.visibility = View.GONE
            return
        }
        mediaBottomRow.visibility = View.VISIBLE
        mediaBottomLeft.load(list.getOrNull(1))
        mediaBottomRight.load(list.getOrNull(2))
        val more = list.size - 3
        mediaMore.visibility = if (more > 0) View.VISIBLE else View.GONE
        mediaMore.text = if (more > 0) "+$more" else ""
    }

    private fun publishPost() {
        val auth = getSharedPreferences("auth", MODE_PRIVATE)
        val login = auth.getString("login", "")?.trim().orEmpty()
        val content = etPostText.text?.toString()?.trim().orEmpty()
        if (login.isBlank()) {
            safeToast(getString(R.string.error_network))
            return
        }
        if (content.isBlank() && attachments.isEmpty() && pollDraft == null) {
            safeToast("Добавьте текст, медиа или опрос")
            return
        }
        if (content.length > MAX_POST_TEXT_LENGTH) {
            safeToast("Максимум $MAX_POST_TEXT_LENGTH символа(ов) в тексте новости", long = true)
            return
        }
        val imageCount = attachments.count { !isVideoUri(it) }
        val videoCount = attachments.count { isVideoUri(it) }
        val isEvent = cbEventCoins.isChecked
        if (imageCount > MAX_POST_IMAGES) {
            safeToast("Максимум $MAX_POST_IMAGES фото в одной новости", long = true)
            return
        }
        if (videoCount > MAX_POST_VIDEOS) {
            safeToast("Максимум $MAX_POST_VIDEOS видео в одной новости", long = true)
            return
        }

        findViewById<View>(R.id.btnPublishPost).isEnabled = false
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    if (attachments.isEmpty()) {
                        ApiClient.postApi.createPost(
                            CreatePostRequest(
                                content = content,
                                authorLogin = login,
                                isImportant = false,
                                poll = pollDraft,
                                isEvent = isEvent,
                                eventCoinReward = null,
                                eventGrantDelayDays = null,
                                eventGrantInstant = null
                            )
                        )
                    } else {
                        val contentBody = content.toRequestBody("text/plain".toMediaTypeOrNull())
                        val loginBody = login.toRequestBody("text/plain".toMediaTypeOrNull())
                        val importantBody = "false".toRequestBody("text/plain".toMediaTypeOrNull())
                        val isEventBody = if (isEvent) "true".toRequestBody("text/plain".toMediaTypeOrNull()) else null
                        val eventCoinsBody = null
                        val eventGrantDelayDaysBody = null
                        val eventGrantInstantBody = null
                        val pollJsonBody = pollDraft?.let {
                            com.google.gson.Gson().toJson(it).toRequestBody("application/json".toMediaTypeOrNull())
                        }
                        val parts = attachments.mapIndexedNotNull { idx, uri ->
                            val mime = contentResolver.getType(uri) ?: "image/jpeg"
                            val bytes = contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: return@mapIndexedNotNull null
                            val rb = bytes.toRequestBody(mime.toMediaTypeOrNull())
                            MultipartBody.Part.createFormData("media", "media_$idx", rb)
                        }
                        ApiClient.postApi.createPostWithMedia(
                            content = contentBody,
                            authorLogin = loginBody,
                            isImportant = importantBody,
                            isEvent = isEventBody,
                            eventCoinReward = eventCoinsBody,
                            eventGrantDelayDays = eventGrantDelayDaysBody,
                            eventGrantInstant = eventGrantInstantBody,
                            pollJson = pollJsonBody,
                            media = parts
                        )
                    }
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                safeToast("Новость опубликована")
                setResult(RESULT_OK)
                finishWithSheetAnimation()
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            } finally {
                findViewById<View>(R.id.btnPublishPost).isEnabled = true
            }
        }
    }

    private fun isVideoUri(uri: Uri): Boolean {
        val mime = contentResolver.getType(uri)?.lowercase().orEmpty()
        if (mime.startsWith("video/")) return true
        val path = uri.lastPathSegment?.lowercase().orEmpty()
        return path.endsWith(".mp4") ||
            path.endsWith(".mov") ||
            path.endsWith(".mkv") ||
            path.endsWith(".webm") ||
            path.endsWith(".avi") ||
            path.endsWith(".m4v") ||
            path.endsWith(".3gp")
    }

    private fun updatePostTextCounter() {
        val len = etPostText.text?.length ?: 0
        postTextCounter.text = "$len/$MAX_POST_TEXT_LENGTH"
    }

    private fun updatePostPreview() {
        val content = etPostText.text?.toString()?.trim().orEmpty()
        if (content.isBlank()) {
            postPreviewContainer.visibility = View.GONE
            postPreviewText.text = ""
            return
        }
        postPreviewContainer.visibility = View.VISIBLE
        postPreviewText.text = content
    }
}

