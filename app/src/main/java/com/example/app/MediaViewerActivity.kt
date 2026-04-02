package com.example.app

import android.net.Uri
import android.os.Bundle
import android.view.View
import android.widget.ImageView
import android.widget.VideoView
import androidx.appcompat.app.AppCompatActivity
import coil.load
import com.google.android.material.button.MaterialButton

class MediaViewerActivity : AppCompatActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_media_viewer)

        val image = findViewById<ImageView>(R.id.viewerImage)
        val video = findViewById<VideoView>(R.id.viewerVideo)
        val close = findViewById<MaterialButton>(R.id.btnCloseViewer)

        close.setOnClickListener { finish() }

        val url = intent.getStringExtra(EXTRA_URL)?.trim().orEmpty()
        if (url.isBlank()) {
            finish()
            return
        }

        if (isVideoUrl(url)) {
            image.visibility = View.GONE
            video.visibility = View.VISIBLE
            video.setVideoURI(Uri.parse(url))
            video.setOnPreparedListener { mp ->
                mp.isLooping = true
                video.start()
            }
        } else {
            video.visibility = View.GONE
            image.visibility = View.VISIBLE
            image.load(url) {
                crossfade(true)
                placeholder(R.drawable.bg_feed_card)
                error(android.R.drawable.ic_dialog_alert)
            }
        }
    }

    override fun onPause() {
        super.onPause()
        findViewById<VideoView>(R.id.viewerVideo).pause()
    }

    private fun isVideoUrl(url: String): Boolean {
        val path = Uri.parse(url).path?.lowercase().orEmpty()
        return path.endsWith(".mp4") || path.endsWith(".mov") || path.endsWith(".webm")
            || path.endsWith(".mkv") || path.endsWith(".3gp") || path.endsWith(".m4v")
    }

    companion object {
        const val EXTRA_URL = "media_url"
    }
}

