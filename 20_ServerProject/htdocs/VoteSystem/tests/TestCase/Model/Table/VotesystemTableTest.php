<?php
declare(strict_types=1);

namespace App\Test\TestCase\Model\Table;

use App\Model\Table\VotesystemTable;
use Cake\TestSuite\TestCase;

/**
 * App\Model\Table\VotesystemTable Test Case
 */
class VotesystemTableTest extends TestCase
{
    /**
     * Test subject
     *
     * @var \App\Model\Table\VotesystemTable
     */
    protected $Votesystem;

    /**
     * Fixtures
     *
     * @var array<string>
     */
    protected array $fixtures = [
        'app.Votesystem',
    ];

    /**
     * setUp method
     *
     * @return void
     */
    protected function setUp(): void
    {
        parent::setUp();
        $config = $this->getTableLocator()->exists('Votesystem') ? [] : ['className' => VotesystemTable::class];
        $this->Votesystem = $this->getTableLocator()->get('Votesystem', $config);
    }

    /**
     * tearDown method
     *
     * @return void
     */
    protected function tearDown(): void
    {
        unset($this->Votesystem);

        parent::tearDown();
    }

    /**
     * Test validationDefault method
     *
     * @return void
     * @link \App\Model\Table\VotesystemTable::validationDefault()
     */
    public function testValidationDefault(): void
    {
        $this->markTestIncomplete('Not implemented yet.');
    }
}
