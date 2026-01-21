import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import './SnowUITest.css';

// 导入 SnowUI 组件
import {
  // 图标组件 - 不同权重
  Add,
  Airplane,
  FourLeafClover,
  Heart,
  Star,
  Gear,
  User,
  SearchIcon,
  House,
  Bell,
  // 头像组件
  Avatar3d01,
  Avatar3d02,
  Avatar3d03,
  AvatarByewind,
  // Logo 组件
  Google,
  Apple,
  Github,
  Facebook,
  Twitter,
  Microsoft,
  // 背景组件
  Gradient01,
  Gradient02,
  Geometric01,
  // 表情符号
  FaceBlowingKiss,
  RedHeart,
  SnowflakeEmoji,
  // 光标
  CursorsBeachball,
  CursorsHandPointing,
  // 插画
  Illustration01,
  Illustration02,
  // 图片
  Image01,
} from '@snowui-design-system/resource-react';

const SnowUITest: React.FC = () => {
  const navigate = useNavigate();
  const [selectedWeight, setSelectedWeight] = useState<'regular' | 'thin' | 'light' | 'bold' | 'fill' | 'duotone'>('regular');

  return (
    <div className="snowui-test-page">
      <div className="snowui-test-container">
        <div className="snowui-test-header">
          <button className="back-button" onClick={() => navigate('/')}>
            ← 返回登录页
          </button>
          <h1>SnowUI 组件测试</h1>
          <p className="subtitle">展示各种 SnowUI 组件的使用示例</p>
        </div>

        {/* 图标权重选择器 */}
        <div className="weight-selector">
          <label>选择图标权重：</label>
          <div className="weight-buttons">
            {(['regular', 'thin', 'light', 'bold', 'fill', 'duotone'] as const).map((weight) => (
              <button
                key={weight}
                className={`weight-btn ${selectedWeight === weight ? 'active' : ''}`}
                onClick={() => setSelectedWeight(weight)}
              >
                {weight}
              </button>
            ))}
          </div>
        </div>

        {/* 图标组件展示 */}
        <section className="component-section">
          <h2>📦 图标组件 (Icons)</h2>
          <p className="section-desc">支持 6 种权重：regular, thin, light, bold, fill, duotone</p>
          <div className="component-grid">
            <div className="component-card">
              <div className="component-preview">
                <Add size={48} weight={selectedWeight} className="text-blue-500" />
              </div>
              <div className="component-info">
                <h3>Add</h3>
                <code>&lt;Add size={48} weight="{selectedWeight}" /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <Airplane size={48} weight={selectedWeight} className="text-green-500" />
              </div>
              <div className="component-info">
                <h3>Airplane</h3>
                <code>&lt;Airplane size={48} weight="{selectedWeight}" /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <FourLeafClover size={48} weight={selectedWeight} className="text-emerald-500" />
              </div>
              <div className="component-info">
                <h3>FourLeafClover</h3>
                <code>&lt;FourLeafClover size={48} weight="{selectedWeight}" /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <Heart size={48} weight={selectedWeight} className="text-red-500" />
              </div>
              <div className="component-info">
                <h3>Heart</h3>
                <code>&lt;Heart size={48} weight="{selectedWeight}" /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <Star size={48} weight={selectedWeight} className="text-yellow-500" />
              </div>
              <div className="component-info">
                <h3>Star</h3>
                <code>&lt;Star size={48} weight="{selectedWeight}" /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <Gear size={48} weight={selectedWeight} className="text-gray-500" />
              </div>
              <div className="component-info">
                <h3>Gear</h3>
                <code>&lt;Gear size={48} weight="{selectedWeight}" /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <User size={48} weight={selectedWeight} className="text-purple-500" />
              </div>
              <div className="component-info">
                <h3>User</h3>
                <code>&lt;User size={48} weight="{selectedWeight}" /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <SearchIcon size={48} weight={selectedWeight} className="text-indigo-500" />
              </div>
              <div className="component-info">
                <h3>SearchIcon</h3>
                <code>&lt;SearchIcon size={48} weight="{selectedWeight}" /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <House size={48} weight={selectedWeight} className="text-orange-500" />
              </div>
              <div className="component-info">
                <h3>House</h3>
                <code>&lt;House size={48} weight="{selectedWeight}" /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <Bell size={48} weight={selectedWeight} className="text-pink-500" />
              </div>
              <div className="component-info">
                <h3>Bell</h3>
                <code>&lt;Bell size={48} weight="{selectedWeight}" /&gt;</code>
              </div>
            </div>
          </div>
        </section>

        {/* 头像组件展示 */}
        <section className="component-section">
          <h2>👤 头像组件 (Avatars)</h2>
          <p className="section-desc">自动尺寸匹配，支持 16×16 到 512×512</p>
          <div className="component-grid">
            <div className="component-card">
              <div className="component-preview avatar-preview">
                <Avatar3d01 size={64} className="rounded-full" />
              </div>
              <div className="component-info">
                <h3>Avatar3d01</h3>
                <code>&lt;Avatar3d01 size={64} /&gt;</code>
                <p className="size-info">自动选择最接近的尺寸</p>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview avatar-preview">
                <Avatar3d02 size={80} className="rounded-full" />
              </div>
              <div className="component-info">
                <h3>Avatar3d02</h3>
                <code>&lt;Avatar3d02 size={80} /&gt;</code>
                <p className="size-info">请求 80px，自动使用 80×80</p>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview avatar-preview">
                <Avatar3d03 size={100} className="rounded-full" />
              </div>
              <div className="component-info">
                <h3>Avatar3d03</h3>
                <code>&lt;Avatar3d03 size={100} /&gt;</code>
                <p className="size-info">请求 100px，自动使用 128×128</p>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview avatar-preview">
                <AvatarByewind size={48} className="rounded-full" />
              </div>
              <div className="component-info">
                <h3>AvatarByewind</h3>
                <code>&lt;AvatarByewind size={48} /&gt;</code>
                <p className="size-info">精确匹配 48×48</p>
              </div>
            </div>
          </div>
        </section>

        {/* Logo 组件展示 */}
        <section className="component-section">
          <h2>🏢 Logo 组件 (Logos)</h2>
          <p className="section-desc">知名品牌 Logo，共 65 个</p>
          <div className="component-grid">
            <div className="component-card">
              <div className="component-preview">
                <Google size={48} />
              </div>
              <div className="component-info">
                <h3>Google</h3>
                <code>&lt;Google size={48} /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <Apple size={48} />
              </div>
              <div className="component-info">
                <h3>Apple</h3>
                <code>&lt;Apple size={48} /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <Github size={48} />
              </div>
              <div className="component-info">
                <h3>Github</h3>
                <code>&lt;Github size={48} /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <Facebook size={48} />
              </div>
              <div className="component-info">
                <h3>Facebook</h3>
                <code>&lt;Facebook size={48} /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <Twitter size={48} />
              </div>
              <div className="component-info">
                <h3>Twitter</h3>
                <code>&lt;Twitter size={48} /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <Microsoft size={48} />
              </div>
              <div className="component-info">
                <h3>Microsoft</h3>
                <code>&lt;Microsoft size={48} /&gt;</code>
              </div>
            </div>
          </div>
        </section>

        {/* 背景组件展示 */}
        <section className="component-section">
          <h2>🎨 背景组件 (Backgrounds)</h2>
          <p className="section-desc">自动宽度匹配：320, 640, 1024, 1920px</p>
          <div className="background-grid">
            <div className="background-card">
              <div className="background-preview">
                <Gradient01 width={300} />
              </div>
              <div className="component-info">
                <h3>Gradient01</h3>
                <code>&lt;Gradient01 width={300} /&gt;</code>
                <p className="size-info">请求 300px，自动使用 320px</p>
              </div>
            </div>
            <div className="background-card">
              <div className="background-preview">
                <Gradient02 width={500} />
              </div>
              <div className="component-info">
                <h3>Gradient02</h3>
                <code>&lt;Gradient02 width={500} /&gt;</code>
                <p className="size-info">请求 500px，自动使用 640px</p>
              </div>
            </div>
            <div className="background-card">
              <div className="background-preview">
                <Geometric01 width={800} />
              </div>
              <div className="component-info">
                <h3>Geometric01</h3>
                <code>&lt;Geometric01 width={800} /&gt;</code>
                <p className="size-info">请求 800px，自动使用 1024px</p>
              </div>
            </div>
          </div>
        </section>

        {/* 表情符号组件展示 */}
        <section className="component-section">
          <h2>😊 表情符号组件 (Emoji)</h2>
          <p className="section-desc">共 25 个表情符号</p>
          <div className="component-grid">
            <div className="component-card">
              <div className="component-preview">
                <FaceBlowingKiss size={64} />
              </div>
              <div className="component-info">
                <h3>FaceBlowingKiss</h3>
                <code>&lt;FaceBlowingKiss size={64} /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <RedHeart size={64} />
              </div>
              <div className="component-info">
                <h3>RedHeart</h3>
                <code>&lt;RedHeart size={64} /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <SnowflakeEmoji size={64} />
              </div>
              <div className="component-info">
                <h3>SnowflakeEmoji</h3>
                <code>&lt;SnowflakeEmoji size={64} /&gt;</code>
              </div>
            </div>
          </div>
        </section>

        {/* 光标组件展示 */}
        <section className="component-section">
          <h2>🖱️ 光标组件 (Cursors)</h2>
          <p className="section-desc">共 21 个光标样式</p>
          <div className="component-grid">
            <div className="component-card">
              <div className="component-preview">
                <CursorsBeachball size={48} />
              </div>
              <div className="component-info">
                <h3>CursorsBeachball</h3>
                <code>&lt;CursorsBeachball size={48} /&gt;</code>
              </div>
            </div>
            <div className="component-card">
              <div className="component-preview">
                <CursorsHandPointing size={48} />
              </div>
              <div className="component-info">
                <h3>CursorsHandPointing</h3>
                <code>&lt;CursorsHandPointing size={48} /&gt;</code>
              </div>
            </div>
          </div>
        </section>

        {/* 插画组件展示 */}
        <section className="component-section">
          <h2>🎨 插画组件 (Illustrations)</h2>
          <p className="section-desc">共 38 个插画，自动宽度匹配</p>
          <div className="illustration-grid">
            <div className="illustration-card">
              <div className="illustration-preview">
                <Illustration01 width={200} />
              </div>
              <div className="component-info">
                <h3>Illustration01</h3>
                <code>&lt;Illustration01 width={200} /&gt;</code>
              </div>
            </div>
            <div className="illustration-card">
              <div className="illustration-preview">
                <Illustration02 width={200} />
              </div>
              <div className="component-info">
                <h3>Illustration02</h3>
                <code>&lt;Illustration02 width={200} /&gt;</code>
              </div>
            </div>
          </div>
        </section>

        {/* 图片组件展示 */}
        <section className="component-section">
          <h2>🖼️ 图片组件 (Images)</h2>
          <p className="section-desc">共 7 个图片组件</p>
          <div className="component-grid">
            <div className="component-card">
              <div className="component-preview">
                <Image01 width={200} />
              </div>
              <div className="component-info">
                <h3>Image01</h3>
                <code>&lt;Image01 width={200} /&gt;</code>
              </div>
            </div>
          </div>
        </section>

        {/* 统计信息 */}
        <section className="stats-section">
          <h2>📊 组件统计</h2>
          <div className="stats-grid">
            <div className="stat-card">
              <div className="stat-number">1,332</div>
              <div className="stat-label">图标组件</div>
            </div>
            <div className="stat-card">
              <div className="stat-number">286</div>
              <div className="stat-label">头像组件</div>
            </div>
            <div className="stat-card">
              <div className="stat-number">65</div>
              <div className="stat-label">Logo 组件</div>
            </div>
            <div className="stat-card">
              <div className="stat-number">27</div>
              <div className="stat-label">背景组件</div>
            </div>
            <div className="stat-card">
              <div className="stat-number">25</div>
              <div className="stat-label">表情符号</div>
            </div>
            <div className="stat-card">
              <div className="stat-number">21</div>
              <div className="stat-label">光标组件</div>
            </div>
            <div className="stat-card">
              <div className="stat-number">38</div>
              <div className="stat-label">插画组件</div>
            </div>
            <div className="stat-card">
              <div className="stat-number">7</div>
              <div className="stat-label">图片组件</div>
            </div>
            <div className="stat-card highlight">
              <div className="stat-number">1,802</div>
              <div className="stat-label">总组件数</div>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
};

export default SnowUITest;
