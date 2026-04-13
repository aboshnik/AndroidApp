package com.example.app

import android.net.Uri
import android.os.Bundle
import android.view.View
import android.widget.EditText
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
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

    private val openPollEditor = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { res ->
        if (res.resultCode != RESULT_OK) return@registerForActivityResult
        val json = res.data?.getStringExtra(PollEditorActivity.EXTRA_POLL_JSON).orEmpty()
        if (json.isBlank()) return@registerForActivityResult
        pollDraft = runCatching {
            com.google.gson.Gson().fromJson(json, PollCreateRequest::class.java)
        }.getOrNull()
        bindPollChip()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_create_post)

        etPostText = findViewById(R.id.etPostText)
        mediaContainer = findViewById(R.id.postMediaContainer)
        mediaTop = findViewById(R.id.postMediaTop)
        mediaBottomRow = findViewById(R.id.postMediaBottomRow)
        mediaBottomLeft = findViewById(R.id.postMediaBottomLeft)
        mediaBottomRight = findViewById(R.id.postMediaBottomRight)
        mediaMore = findViewById(R.id.postMediaMoreOverlay)
        pollChip = findViewById(R.id.tvPollChip)

        findViewById<View>(R.id.btnCreatePostBack).setOnClickListener { finish() }
        findViewById<View>(R.id.btnAttachMedia).setOnClickListener { pickMedia.launch(arrayOf("image/*", "video/*")) }
        findViewById<View>(R.id.btnAttachPoll).setOnClickListener {
            val i = android.content.Intent(this, PollEditorActivity::class.java)
            pollDraft?.let { i.putExtra(PollEditorActivity.EXTRA_POLL_JSON, com.google.gson.Gson().toJson(it)) }
            openPollEditor.launch(i)
        }
        findViewById<View>(R.id.btnPublishPost).setOnClickListener { publishPost() }
        findViewById<View>(R.id.btnClearPoll).setOnClickListener {
            pollDraft = null
            bindPollChip()
        }

        bindMediaPreview()
        bindPollChip()
    }

    private fun bindPollChip() {
        pollChip.visibility = if (pollDraft == null) View.GONE else View.VISIBLE
        pollChip.text = if (pollDraft == null) "" else "Опрос: ${pollDraft?.question.orEmpty()}"
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
                                poll = pollDraft
                            )
                        )
                    } else {
                        val contentBody = content.toRequestBody("text/plain".toMediaTypeOrNull())
                        val loginBody = login.toRequestBody("text/plain".toMediaTypeOrNull())
                        val importantBody = "false".toRequestBody("text/plain".toMediaTypeOrNull())
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
                finish()
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
}

