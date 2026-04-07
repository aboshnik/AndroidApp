package com.example.app

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.View
import android.widget.RadioButton
import android.widget.Toast
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.example.app.api.ApiClient
import com.example.app.api.CreatePostRequest
import com.example.app.api.PollCreateRequest
import com.google.gson.Gson
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import android.webkit.MimeTypeMap
import java.io.File
import java.io.FileOutputStream

class CreatePostActivity : BaseActivity() {
    private val attachments = mutableListOf<Uri>()
    private lateinit var attachmentsAdapter: AttachmentsAdapter
    private var login: String = ""
    private var pollData: PollCreateRequest? = null
    private val gson = Gson()

    private val pickAttachments = registerForActivityResult(ActivityResultContracts.PickMultipleVisualMedia(10)) { uris ->
        if (uris.isNullOrEmpty()) return@registerForActivityResult
        uris.forEach { uri ->
            runCatching {
                contentResolver.takePersistableUriPermission(
                    uri,
                    Intent.FLAG_GRANT_READ_URI_PERMISSION
                )
            }
            if (!attachments.contains(uri)) attachments.add(uri)
        }
        refreshAttachments()
        Toast.makeText(this, getString(R.string.attachments_added, uris.size), Toast.LENGTH_SHORT).show()
    }

    private val openPollEditor = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { res ->
        if (res.resultCode != RESULT_OK) return@registerForActivityResult
        val data = res.data ?: return@registerForActivityResult
        if (data.getBooleanExtra(PollEditorActivity.EXTRA_DELETE_POLL, false)) {
            pollData = null
            updatePollButtonUi()
            return@registerForActivityResult
        }
        val pollJson = data.getStringExtra(PollEditorActivity.EXTRA_POLL_JSON).orEmpty()
        if (pollJson.isNotBlank()) {
            pollData = runCatching { gson.fromJson(pollJson, PollCreateRequest::class.java) }.getOrNull()
            updatePollButtonUi()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_create_post)

        login = intent.getStringExtra("login") ?: ""
        if (login.isBlank()) {
            Toast.makeText(this, getString(R.string.error_network), Toast.LENGTH_SHORT).show()
            finish()
            return
        }

        val recycler = findViewById<RecyclerView>(R.id.recyclerAttachments)
        attachmentsAdapter = AttachmentsAdapter(attachments) { uri ->
            attachments.remove(uri)
            refreshAttachments()
        }
        recycler.layoutManager = LinearLayoutManager(this, LinearLayoutManager.HORIZONTAL, false)
        recycler.adapter = attachmentsAdapter

        findViewById<View>(R.id.btnClose).setOnClickListener { finish() }

        findViewById<View>(R.id.btnNext).setOnClickListener {
            val text = findViewById<TextInputEditText>(R.id.etPostText).text?.toString()?.trim()
            if (text.isNullOrBlank()) {
                Toast.makeText(this, getString(R.string.create_post_hint), Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            val isImportant = findViewById<RadioButton>(R.id.rbPostImportant).isChecked
            publishPost(text, login, isImportant, pollData)
        }

        val tilPost = findViewById<TextInputLayout>(R.id.tilPost)
        tilPost.setEndIconOnClickListener { showAttachMenu() }

        updatePollButtonUi()
    }

    override fun onPause() {
        super.onPause()
        SessionManager.touch(this)
    }

    private fun refreshAttachments() {
        attachmentsAdapter.replaceAll(attachments.toList())
        findViewById<View>(R.id.recyclerAttachments).visibility = if (attachments.isEmpty()) View.GONE else View.VISIBLE
    }

    private fun publishPost(content: String, login: String, isImportant: Boolean, poll: PollCreateRequest?) {
        scope.launch {
            try {
                val response = withContext(Dispatchers.IO) {
                    val first = attachments.firstOrNull()
                    if (first == null) {
                        ApiClient.postApi.createPost(
                            CreatePostRequest(
                                content = content,
                                authorLogin = login,
                                isImportant = isImportant,
                                poll = poll
                            )
                        )
                    } else {
                        val contentRb = content.toRequestBody("text/plain".toMediaTypeOrNull())
                        val loginRb = login.toRequestBody("text/plain".toMediaTypeOrNull())
                        val importantRb = isImportant.toString().toRequestBody("text/plain".toMediaTypeOrNull())
                        val pollJsonRb = poll?.let { Gson().toJson(it).toRequestBody("application/json".toMediaTypeOrNull()) }
                        val part = buildMultipartFromUri(first)
                            ?: throw IllegalStateException("Не удалось прочитать выбранный файл. Выберите файл заново.")
                        ApiClient.postApi.createPostWithMedia(
                            content = contentRb,
                            authorLogin = loginRb,
                            isImportant = importantRb,
                            pollJson = pollJsonRb,
                            media = part
                        )
                    }
                }
                val body = response.body()
                if (response.isSuccessful && body != null && body.success) {
                    Toast.makeText(this@CreatePostActivity, body.message, Toast.LENGTH_SHORT).show()
                    setResult(RESULT_OK)
                    finish()
                } else {
                    Toast.makeText(
                        this@CreatePostActivity,
                        body?.message ?: getString(R.string.error_network),
                        Toast.LENGTH_LONG
                    ).show()
                }
            } catch (e: Exception) {
                Toast.makeText(
                    this@CreatePostActivity,
                    "${getString(R.string.error_network)} ${e.message}",
                    Toast.LENGTH_LONG
                ).show()
            }
        }
    }

    private fun buildMultipartFromUri(uri: Uri): MultipartBody.Part? {
        val mime = contentResolver.getType(uri)
        val ext = resolveExtension(uri, mime)
        val finalMime = mime ?: MimeTypeMap.getSingleton()
            .getMimeTypeFromExtension(ext.removePrefix(".").lowercase())
            ?: "application/octet-stream"

        val tmp = File(cacheDir, "upload_${System.currentTimeMillis()}$ext")
        contentResolver.openInputStream(uri)?.use { input ->
            FileOutputStream(tmp).use { out -> input.copyTo(out) }
        } ?: return null

        val rb = tmp.asRequestBody(finalMime.toMediaTypeOrNull())
        return MultipartBody.Part.createFormData("media", tmp.name, rb)
    }

    private fun resolveExtension(uri: Uri, mime: String?): String {
        // 1) By mime when available
        if (!mime.isNullOrBlank()) {
            val byMime = MimeTypeMap.getSingleton().getExtensionFromMimeType(mime)?.trim()
            if (!byMime.isNullOrBlank()) return ".${byMime.lowercase()}"
            if (mime.startsWith("video/")) return ".mp4"
            if (mime.startsWith("image/")) return ".jpg"
        }

        // 2) By URI path (works for many gallery providers)
        val pathExt = MimeTypeMap.getFileExtensionFromUrl(uri.toString())?.trim()
        if (!pathExt.isNullOrBlank()) return ".${pathExt.lowercase()}"

        // 3) Safe fallback
        return ".bin"
    }

    private fun updatePollButtonUi() {
        val status = findViewById<android.widget.TextView>(R.id.tvPollStatus)
        if (pollData == null) {
            status.visibility = View.GONE
        } else {
            status.text = "Опрос: настроен"
            status.visibility = View.VISIBLE
        }
    }

    private fun showAttachMenu() {
        val items = arrayOf("Фото/видео", "Опрос")
        val builder = androidx.appcompat.app.AlertDialog.Builder(this)
            .setTitle("Добавить")
            .setItems(items) { _, which ->
                when (which) {
                    0 -> pickAttachments.launch(
                        PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageAndVideo)
                    )
                    1 -> {
                        val i = Intent(this, PollEditorActivity::class.java)
                        pollData?.let { i.putExtra(PollEditorActivity.EXTRA_POLL_JSON, gson.toJson(it)) }
                        openPollEditor.launch(i)
                    }
                }
            }
        safeShowDialog(builder)
    }
}
