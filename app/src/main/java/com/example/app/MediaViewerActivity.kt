package com.example.app

import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.View
import android.widget.ImageView
import android.widget.SeekBar
import android.widget.TextView
import android.widget.VideoView
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import coil.load

class MediaViewerActivity : BaseActivity() {
    companion object {
        const val EXTRA_URL = "url"
        const val EXTRA_MEDIA_URLS = "media_urls"
        const val EXTRA_START_INDEX = "start_index"
    }

    private lateinit var iv: ImageView
    private lateinit var vv: VideoView
    private lateinit var tvErr: TextView
    private lateinit var btnPlayPause: TextView
    private lateinit var seekVideo: SeekBar
    private lateinit var tvCurrent: TextView
    private lateinit var tvDuration: TextView
    private lateinit var videoControls: View
    private lateinit var thumbsRecycler: RecyclerView

    private val ui = Handler(Looper.getMainLooper())
    private var mediaUrls: List<String> = emptyList()
    private var currentIndex: Int = 0
    private var userSeeking = false
    private val progressTicker = object : Runnable {
        override fun run() {
            if (::vv.isInitialized && vv.visibility == View.VISIBLE && vv.isPlaying && !userSeeking) {
                seekVideo.progress = vv.currentPosition
                tvCurrent.text = formatMs(vv.currentPosition)
            }
            ui.postDelayed(this, 250L)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_media_viewer)

        iv = findViewById(R.id.ivMediaFull)
        vv = findViewById(R.id.vvMediaFull)
        tvErr = findViewById(R.id.tvMediaError)
        btnPlayPause = findViewById(R.id.btnVideoPlayPause)
        seekVideo = findViewById(R.id.seekVideo)
        tvCurrent = findViewById(R.id.tvVideoCurrent)
        tvDuration = findViewById(R.id.tvVideoDuration)
        videoControls = findViewById(R.id.videoControls)
        thumbsRecycler = findViewById(R.id.recyclerMediaThumbs)

        findViewById<View>(R.id.btnCloseMedia).setOnClickListener { finish() }
        findViewById<View>(R.id.btnPrevMedia).setOnClickListener { showAt(currentIndex - 1) }
        findViewById<View>(R.id.btnNextMedia).setOnClickListener { showAt(currentIndex + 1) }
        setupVideoControls()

        val startUrl = intent.getStringExtra(EXTRA_URL)?.trim().orEmpty()
        val incoming = intent.getStringArrayListExtra(EXTRA_MEDIA_URLS).orEmpty().map { it.trim() }.filter { it.isNotEmpty() }
        mediaUrls = if (incoming.isNotEmpty()) incoming else listOf(startUrl).filter { it.isNotBlank() }
        currentIndex = intent.getIntExtra(EXTRA_START_INDEX, 0).coerceIn(0, (mediaUrls.size - 1).coerceAtLeast(0))

        if (mediaUrls.isEmpty()) {
            tvErr.visibility = View.VISIBLE
            tvErr.text = "Не удалось открыть медиа"
            return
        }

        thumbsRecycler.layoutManager = LinearLayoutManager(this, LinearLayoutManager.HORIZONTAL, false)
        thumbsRecycler.adapter = ThumbsAdapter(mediaUrls) { idx -> showAt(idx) }
        thumbsRecycler.visibility = if (mediaUrls.size > 1) View.VISIBLE else View.GONE

        showAt(currentIndex)
        ui.post(progressTicker)
    }

    override fun onPause() {
        super.onPause()
        runCatching { vv.pause() }
    }

    override fun onDestroy() {
        ui.removeCallbacks(progressTicker)
        runCatching { vv.stopPlayback() }
        super.onDestroy()
    }

    private fun showAt(index: Int) {
        if (mediaUrls.isEmpty()) return
        currentIndex = index.coerceIn(0, mediaUrls.lastIndex)
        val url = mediaUrls[currentIndex]
        tvErr.visibility = View.GONE

        (thumbsRecycler.adapter as? ThumbsAdapter)?.setSelected(currentIndex)
        findViewById<View>(R.id.btnPrevMedia).visibility = if (currentIndex > 0) View.VISIBLE else View.INVISIBLE
        findViewById<View>(R.id.btnNextMedia).visibility = if (currentIndex < mediaUrls.lastIndex) View.VISIBLE else View.INVISIBLE

        if (isVideoUrl(url)) {
            vv.visibility = View.VISIBLE
            iv.visibility = View.GONE
            videoControls.visibility = View.VISIBLE
            thumbsRecycler.visibility = View.GONE
            seekVideo.progress = 0
            seekVideo.max = 1
            tvCurrent.text = "00:00"
            tvDuration.text = "00:00"
            btnPlayPause.text = "⏸"

            vv.setVideoURI(Uri.parse(url))
            vv.setOnPreparedListener { mp ->
                mp.isLooping = false
                seekVideo.max = mp.duration.coerceAtLeast(1)
                tvDuration.text = formatMs(mp.duration)
                vv.start()
                btnPlayPause.text = "⏸"
            }
            vv.setOnErrorListener { _, _, _ ->
                tvErr.visibility = View.VISIBLE
                tvErr.text = "Ошибка воспроизведения видео"
                videoControls.visibility = View.GONE
                true
            }
            vv.setOnCompletionListener {
                btnPlayPause.text = "▶"
            }
        } else {
            iv.visibility = View.VISIBLE
            vv.visibility = View.GONE
            videoControls.visibility = View.GONE
            thumbsRecycler.visibility = if (mediaUrls.size > 1) View.VISIBLE else View.GONE
            runCatching { vv.stopPlayback() }
            iv.load(url) {
                crossfade(true)
                error(R.drawable.ic_launcher_simple)
            }
        }
    }

    private fun setupVideoControls() {
        btnPlayPause.setOnClickListener {
            if (vv.visibility != View.VISIBLE) return@setOnClickListener
            if (vv.isPlaying) {
                vv.pause()
                btnPlayPause.text = "▶"
            } else {
                vv.start()
                btnPlayPause.text = "⏸"
            }
        }
        seekVideo.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(seekBar: SeekBar?, progress: Int, fromUser: Boolean) {
                if (fromUser) tvCurrent.text = formatMs(progress)
            }

            override fun onStartTrackingTouch(seekBar: SeekBar?) {
                userSeeking = true
            }

            override fun onStopTrackingTouch(seekBar: SeekBar?) {
                val p = seekBar?.progress ?: 0
                runCatching { vv.seekTo(p) }
                userSeeking = false
            }
        })
    }

    private fun formatMs(ms: Int): String {
        val sec = (ms / 1000).coerceAtLeast(0)
        val m = sec / 60
        val s = sec % 60
        return "%02d:%02d".format(m, s)
    }

    private fun isVideoUrl(url: String): Boolean {
        val s = url.lowercase()
        return s.endsWith(".mp4") || s.endsWith(".mov") || s.endsWith(".m4v") || s.endsWith(".webm")
    }

    private class ThumbsAdapter(
        private val urls: List<String>,
        private val onClick: (Int) -> Unit
    ) : RecyclerView.Adapter<ThumbsAdapter.VH>() {
        private var selected = 0

        class VH(v: View) : RecyclerView.ViewHolder(v) {
            val img: ImageView = v.findViewById(R.id.ivThumbMedia)
        }

        override fun onCreateViewHolder(parent: android.view.ViewGroup, viewType: Int): VH {
            val v = android.view.LayoutInflater.from(parent.context)
                .inflate(R.layout.item_media_thumb, parent, false)
            return VH(v)
        }

        override fun getItemCount(): Int = urls.size

        override fun onBindViewHolder(holder: VH, position: Int) {
            val url = urls[position]
            holder.img.load(url) {
                crossfade(true)
                error(R.drawable.ic_launcher_simple)
            }
            holder.itemView.alpha = if (position == selected) 1f else 0.55f
            holder.itemView.setOnClickListener { onClick(position) }
        }

        fun setSelected(index: Int) {
            val prev = selected
            selected = index.coerceIn(0, urls.lastIndex.coerceAtLeast(0))
            notifyItemChanged(prev)
            notifyItemChanged(selected)
        }
    }
}
