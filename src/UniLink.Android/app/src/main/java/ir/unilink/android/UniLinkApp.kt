package ir.unilink.android

import android.app.Application

class UniLinkApp : Application() {
    lateinit var store: Store
        private set
    lateinit var engine: Engine
        private set

    override fun onCreate() {
        super.onCreate()
        store = Store(this)
        engine = Engine(this, store)
    }
}
